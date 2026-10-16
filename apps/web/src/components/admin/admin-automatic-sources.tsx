"use client";

import { useEffect, useRef, useState } from "react";
import { apiGet, apiWrite, ApiResponseError, downloadJson } from "@/lib/api";
import type { AdminBook, GenerationSession, SourcePreparationAvailability, SourcePreparationStatus } from "@/lib/admin-types";

type Props = {
  book: AdminBook; disabled: boolean;
  availability: SourcePreparationAvailability | null;
  onWorkingChange: (working: boolean) => void;
  onStarted: (session: GenerationSession) => void | Promise<void>;
};
type Working = "collect" | "refresh" | "generate" | "cancel" | null;
type Operation = { operationId: string; expectedRevision: number };
const button = "min-h-11 rounded-lg border border-[var(--line)] px-4 py-2 text-sm disabled:opacity-50";
const primary = `${button} bg-[var(--action-surface)] text-[var(--action-text)]`;
const stateLabels = { Collecting: "Coletando fontes", Ready: "Fontes prontas para conferência", Partial: "Coleta parcial", Cancelled: "Coleta cancelada" };
const unitLabels = { Pending: "Aguardando coleta", Collected: "Resumo coletado", MissingSource: "Sem resumo disponível", Failed: "Falha na coleta" };
function message(cause: unknown) {
  if (cause instanceof ApiResponseError && cause.status === 429) {
    const sourceMessage = cause.code ? `${cause.message} ` : "";
    const wait = cause.retryAfterSeconds && cause.retryAfterSeconds > 0 ? `${cause.retryAfterSeconds}s` : "cerca de um minuto";
    return `${sourceMessage}O limite de requisições foi atingido e a operação foi pausada. Aguarde ${wait} antes de continuar.`;
  }
  return cause instanceof Error ? cause.message : "Não foi possível preparar as fontes.";
}
const minimumStepIntervalMs = 2100;

function waitBeforeNextStep(milliseconds: number, signal: AbortSignal) {
  signal.throwIfAborted();
  if (milliseconds <= 0) return Promise.resolve();
  return new Promise<void>((resolve, reject) => {
    const onAbort = () => { clearTimeout(timer); reject(signal.reason); };
    const timer = setTimeout(() => { signal.removeEventListener("abort", onAbort); resolve(); }, milliseconds);
    signal.addEventListener("abort", onAbort, { once: true });
  });
}

export function AdminAutomaticSources({ book, disabled, availability, onWorkingChange, onStarted }: Props) {
  const base = `/api/admin/books/${book.id}/source-preparations`;
  const [preparation, setPreparation] = useState<SourcePreparationStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [expired, setExpired] = useState(false);
  const [working, setWorking] = useState<Working>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [editionConfirmed, setEditionConfirmed] = useState(false);
  const [indexComplete, setIndexComplete] = useState(false);
  const [partialConfirmed, setPartialConfirmed] = useState(false);
  const controller = useRef<AbortController | null>(null);
  const preparationRef = useRef<SourcePreparationStatus | null>(null);
  const pending = useRef<{ kind: "next" | "generate"; operation: Operation } | null>(null);
  const lastStepStarted = useRef<number | null>(null);
  const mounted = useRef(true);
  const formDisabled = disabled || loading || working !== null;
  const canStartCollection = availability?.supported === true;
  const missing = preparation?.units.some((unit) => unit.status !== "Collected") ?? false;
  const hasSummary = preparation?.units.some((unit) => unit.summaryBytes > 0) ?? false;
  const partial = missing || !indexComplete || preparation?.state === "Partial" || preparation?.state === "Cancelled";

  function update(updated: SourcePreparationStatus) {
    preparationRef.current = updated;
    setPreparation(updated);
    setExpired(false);
  }

  useEffect(() => {
    mounted.current = true;
    const loadingRequest = new AbortController();
    apiGet<SourcePreparationStatus>(`${base}/current`, { signal: loadingRequest.signal }).then((current) => {
      if (loadingRequest.signal.aborted) return;
      preparationRef.current = current;
      setPreparation(current);
      setNotice("Preparação recuperada do servidor. Confira a cobertura antes de continuar.");
    }).catch((cause) => {
      if (!loadingRequest.signal.aborted && !(cause instanceof ApiResponseError && cause.status === 404)) setError(message(cause));
    }).finally(() => { if (!loadingRequest.signal.aborted) setLoading(false); });
    return () => {
      mounted.current = false;
      loadingRequest.abort();
      if (controller.current) {
        controller.current.abort();
        controller.current = null;
        onWorkingChange(false);
      }
    };
  }, [base, onWorkingChange]);

  function begin(action: NonNullable<Working>) {
    if (controller.current) return null;
    const current = new AbortController();
    controller.current = current;
    setWorking(action);
    onWorkingChange(true);
    setError("");
    setNotice("");
    return current;
  }

  function finish(current: AbortController) {
    if (controller.current !== current) return;
    controller.current = null;
    if (mounted.current) { setWorking(null); onWorkingChange(false); }
  }

  async function refreshStatus(signal: AbortSignal) {
    const known = preparationRef.current;
    let updated: SourcePreparationStatus;
    try {
      updated = await apiGet<SourcePreparationStatus>(known ? `${base}/${known.preparationId}` : `${base}/current`, { signal });
    } catch (cause) {
      if (!signal.aborted && known && cause instanceof ApiResponseError && cause.status === 404) {
        setExpired(true);
        pending.current = null;
      }
      throw cause;
    }
    if (pending.current && updated.revision !== pending.current.operation.expectedRevision) pending.current = null;
    update(updated);
    return updated;
  }

  async function collect(startNew = false) {
    if (formDisabled || expired && !startNew) return;
    if ((startNew || !preparationRef.current) && !canStartCollection) return;
    const current = begin("collect");
    if (!current) return;
    try {
      if (startNew) { pending.current = null; setEditionConfirmed(false); setIndexComplete(false); setPartialConfirmed(false); }
      let progress = preparationRef.current && !startNew
        ? await refreshStatus(current.signal)
        : await apiWrite<SourcePreparationStatus>(base, "POST", undefined, { signal: current.signal });
      update(progress);
      while (progress.state === "Collecting" && progress.completedUnits < progress.totalUnits) {
        current.signal.throwIfAborted();
        const operation = pending.current?.kind === "next" ? pending.current.operation : { operationId: crypto.randomUUID(), expectedRevision: progress.revision };
        pending.current = { kind: "next", operation };
        if (lastStepStarted.current !== null) await waitBeforeNextStep(minimumStepIntervalMs - (performance.now() - lastStepStarted.current), current.signal);
        current.signal.throwIfAborted();
        lastStepStarted.current = performance.now();
        progress = await apiWrite<SourcePreparationStatus>(`${base}/${progress.preparationId}/next`, "POST", operation, { signal: current.signal });
        pending.current = null;
        update(progress);
      }
      setNotice("Coleta encerrada. Confira a obra, a sequência dos capítulos, as lacunas e a origem dos resumos antes de gerar.");
    } catch (cause) {
      if (!current.signal.aborted) {
        const resume = cause instanceof ApiResponseError && cause.status === 429 && preparationRef.current?.state === "Collecting" ? " Use “Continuar coleta” para retomar os capítulos pendentes." : "";
        setError(`${message(cause)}${resume} Não haverá nova tentativa automática.`);
        try { await refreshStatus(current.signal); }
        catch { setNotice("Consulte o progresso para recuperar o último estado disponível. Fontes já coletadas permanecem na sessão temporária."); }
      }
    } finally { finish(current); }
  }

  async function refresh() {
    if (formDisabled) return;
    const current = begin("refresh");
    if (!current) return;
    try { await refreshStatus(current.signal); }
    catch (cause) { if (!current.signal.aborted) setError(message(cause)); }
    finally { finish(current); }
  }

  async function generate() {
    if (formDisabled || expired || !preparation || !editionConfirmed || !hasSummary || preparation.completedUnits !== preparation.totalUnits || partial && !partialConfirmed) return;
    const current = begin("generate");
    if (!current) return;
    let generated: GenerationSession | null = null;
    try {
      const progress = await refreshStatus(current.signal);
      const operation = pending.current?.kind === "generate" ? pending.current.operation : { operationId: crypto.randomUUID(), expectedRevision: progress.revision };
      pending.current = { kind: "generate", operation };
      generated = await apiWrite<GenerationSession>(`${base}/${progress.preparationId}/generate`, "POST", { ...operation, editionConfirmed, indexComplete }, { signal: current.signal });
      pending.current = null;
    } catch (cause) {
      if (!current.signal.aborted) {
        setError(`${message(cause)} A geração não será repetida automaticamente; consulte o progresso antes de continuar.`);
        try { await refreshStatus(current.signal); } catch { /* Preserve the visible coverage if the server is unavailable. */ }
      }
    } finally { finish(current); }
    if (generated && !current.signal.aborted && mounted.current) await onStarted(generated);
  }

  async function cancel() {
    controller.current?.abort();
    controller.current = null;
    const current = begin("cancel");
    if (!current) return;
    try {
      const progress = preparationRef.current ?? await refreshStatus(current.signal);
      update(await apiWrite<SourcePreparationStatus>(`${base}/${progress.preparationId}/cancel`, "POST", undefined, { signal: current.signal }));
      pending.current = null;
      setNotice("Coleta cancelada. Os resumos já concluídos continuam disponíveis para revisão enquanto a sessão existir.");
    } catch (cause) { if (!current.signal.aborted) setError(message(cause)); }
    finally { finish(current); }
  }

  return (
    <section aria-labelledby="automatic-sources-title" className="mt-5 min-w-0 rounded-xl border border-[var(--line)] bg-[var(--paper)] p-4">
      <h3 id="automatic-sources-title" className="text-xl font-semibold">Buscar capítulos e resumos automaticamente</h3>
      <p className="mt-2 text-sm text-[var(--ink-soft)]">O serviço procura uma fonte pública compatível, coleta os resumos em ordem e os entrega à IA para organizar o pacote. Você acompanha a cobertura e revisa o rascunho antes da importação.</p>
      <p className="mt-2 text-sm text-[var(--ink-soft)]">A cobertura depende da obra e da fonte disponível. Nesta etapa, os resumos podem estar em inglês; confira a tradução dos nomes e a correspondência com sua edição. As telas de revisão podem conter spoilers do livro inteiro.</p>
      <div className="mt-3 rounded-lg border border-[var(--line)] p-3 text-sm">
        {availability ? <>
          <p className="font-semibold">Obras com coleta automática disponível</p>
          <ul className="mt-2 list-disc space-y-1 pl-5">{availability.supportedTitles.map((title) => <li key={title}>{title}</li>)}</ul>
          <p role="status" className="mt-3">{availability.supported ? "Este livro está coberto pela coleta automática. Confira a correspondência com sua edição antes de gerar." : availability.reason ?? "Este livro ainda não possui coleta automática. Use a entrada manual abaixo."}</p>
        </> : <p role="status">Verificando se este livro tem coleta automática disponível…</p>}
      </div>
      {loading && <p role="status" className="mt-3 text-sm">Verificando preparação anterior…</p>}
      {error && <p role="alert" className="form-alert mt-3 rounded-lg border p-3 text-sm">{error}</p>}
      {notice && <p role="status" className="mt-3 text-sm">{notice}</p>}
      {expired && <p role="status" className="mt-3 rounded-lg border border-[var(--line)] p-3 text-sm">A sessão não está mais disponível no servidor. Você ainda pode baixar o relatório visível com prévias e referências.{canStartCollection ? " Também é possível iniciar uma nova coleta." : ""}</p>}
      {!preparation && <button type="button" disabled={formDisabled || !canStartCollection} className={`${primary} mt-4`} onClick={() => void collect()}>{working === "collect" ? "Buscando fontes…" : "Buscar e preparar fontes"}</button>}
      {preparation && <div className="mt-4 min-w-0">
        <p role="status" className="font-semibold">{stateLabels[preparation.state]}</p>
        <p className="mt-2 break-words text-sm">Obra da fonte: <strong>{preparation.sourceBookTitle}</strong> · idioma {preparation.language}</p>
        <p className="mt-2 text-sm">{preparation.completedUnits} de {preparation.totalUnits} capítulos consultados. {preparation.units.filter((unit) => unit.summaryBytes > 0).length} com resumo.</p>
        <progress value={preparation.completedUnits} max={Math.max(1, preparation.totalUnits)} aria-label="Capítulos consultados" className="mt-3 w-full accent-[var(--olive)]" />
        <p className="mt-2 break-words text-xs text-[var(--ink-soft)]">{preparation.attribution} · {preparation.license}</p>
        {preparation.sourceUrl && <a href={preparation.sourceUrl} target="_blank" rel="noopener noreferrer" className="mt-2 inline-flex min-h-11 items-center break-all text-sm underline">Abrir fonte dos resumos</a>}
        <details className="mt-3 min-w-0 rounded-lg border border-[var(--line)] p-3">
          <summary className="min-h-11 cursor-pointer py-2 text-sm font-semibold">Conferir capítulos, resumos e lacunas ({preparation.units.length})</summary>
          <ol className="mt-2 grid max-h-96 gap-3 overflow-auto text-sm">{preparation.units.map((unit) => <li key={unit.order} className="min-w-0 border-t border-[var(--line)] pt-3">
            <p className="break-words"><strong>{unit.order}. {unit.title}</strong> · {unitLabels[unit.status] ?? unit.status}</p>
            {unit.summaryPreview && <p className="mt-2 whitespace-pre-wrap break-words text-[var(--ink-soft)]">{unit.summaryPreview}</p>}
            {unit.sourceUrl && <a href={unit.sourceUrl} target="_blank" rel="noopener noreferrer" className="mt-1 inline-flex min-h-11 items-center break-all underline">Fonte do capítulo</a>}
            {unit.sourceHash && <p className="mt-1 break-all text-xs text-[var(--ink-soft)]">SHA-256: {unit.sourceHash}{unit.revisionId ? ` · revisão ${unit.revisionId}` : ""}</p>}
          </li>)}</ol>
        </details>
        {preparation.diagnostics.length > 0 && <details className="mt-3"><summary className="min-h-11 cursor-pointer py-2 text-sm font-semibold">Alertas das fontes ({preparation.diagnostics.length})</summary><ul className="list-disc space-y-2 pl-5 text-sm">{preparation.diagnostics.map((diagnostic, index) => <li key={index} className="break-words">{diagnostic}</li>)}</ul></details>}
        <div className="mt-4 flex flex-wrap gap-2">
          {preparation.state === "Collecting" && !expired && <button type="button" disabled={formDisabled} className={primary} onClick={() => void collect()}>Continuar coleta</button>}
          {(preparation.state === "Cancelled" || expired) && <button type="button" disabled={formDisabled || !canStartCollection} className={primary} onClick={() => void collect(true)}>Iniciar nova coleta</button>}
          <button type="button" disabled={formDisabled || expired} className={button} onClick={() => void refresh()}>Consultar coleta</button>
          <button type="button" disabled={working !== null} className={button} onClick={() => downloadJson(`mnemora-${book.id}-fontes-coletadas.json`, JSON.stringify(preparation, null, 2))}>Baixar relatório da coleta</button>
          {preparation.state !== "Cancelled" && !expired && <button type="button" disabled={working === "cancel" || disabled} className={button} onClick={() => void cancel()}>{working === "cancel" ? "Cancelando…" : preparation.state === "Collecting" ? "Cancelar coleta" : "Descartar preparação"}</button>}
        </div>
        {(preparation.state === "Ready" || preparation.state === "Partial") && <div className="mt-5 grid gap-3 border-t border-[var(--line)] pt-4">
          <label className="flex min-h-11 items-start gap-3 text-sm"><input type="checkbox" disabled={formDisabled} checked={editionConfirmed} onChange={(event) => setEditionConfirmed(event.target.checked)} className="mt-1 shrink-0" /><span>Conferi que a fonte corresponde a <strong>{book.title}</strong>, de {book.author}, e que a sequência serve para esta edição.</span></label>
          <label className="flex min-h-11 items-start gap-3 text-sm"><input type="checkbox" disabled={formDisabled} checked={indexComplete} onChange={(event) => { setIndexComplete(event.target.checked); setPartialConfirmed(false); }} className="mt-1 shrink-0" /><span>Conferi que o índice está completo. Sem esta confirmação, o pacote será parcial.</span></label>
          {partial && <label className="flex min-h-11 items-start gap-3 text-sm"><input type="checkbox" disabled={formDisabled} checked={partialConfirmed} onChange={(event) => setPartialConfirmed(event.target.checked)} className="mt-1 shrink-0" /><span>Reconheço as lacunas ou o índice não confirmado e quero gerar um rascunho parcial, sem inventar o conteúdo ausente.</span></label>}
          <p className="text-sm text-[var(--ink-soft)]">Os resumos coletados serão enviados ao Groq. A IA extrai o conhecimento por capítulo; importação e publicação continuam sob sua revisão.</p>
          <button type="button" className={`${primary} justify-self-start`} disabled={formDisabled || expired || !editionConfirmed || !hasSummary || partial && !partialConfirmed} onClick={() => void generate()}>{working === "generate" ? "Preparando geração…" : "Gerar pacote com as fontes coletadas"}</button>
          {!hasSummary && <p className="text-sm">Nenhum resumo utilizável foi coletado. Consulte os alertas ou use a entrada manual abaixo.</p>}
        </div>}
      </div>}
      {!preparation && working === "collect" && <button type="button" className={`${button} mt-3`} onClick={() => void cancel()}>Cancelar busca</button>}
      <p className="mt-3 text-xs text-[var(--ink-soft)]">A coleta permanece apenas na sessão temporária do servidor. O relatório contém prévias e referências; não é um pacote para importação.</p>
    </section>
  );
}
