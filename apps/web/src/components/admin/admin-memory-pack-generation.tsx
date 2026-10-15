"use client";

import Link from "next/link";
import { useEffect, useMemo, useRef, useState } from "react";
import { apiGet, apiWrite, ApiResponseError, downloadJson } from "@/lib/api";
import type {
  AdminBook, GeneratedMemoryPackSelection, GenerationAvailability, GenerationEvidence,
  GenerationResult, GenerationSession, GenerationSourceParseResult,
} from "@/lib/admin-types";

type Props = {
  book: AdminBook; disabled: boolean; disabledReason?: string;
  onWorkingChange: (working: boolean) => void;
  onGenerated: (selection: GeneratedMemoryPackSelection) => void;
};
type Outline = { attribution: string; sourceUrl: string; items: { title: string; level: number; page: number | null }[] };
type Working = "parse" | "outline" | "run" | "refresh" | "result" | "cancel" | "configuration" | null;
type Operation = { operationId: string; expectedRevision: number };
const button = "min-h-11 rounded-lg border border-[var(--line)] px-4 py-2 text-sm disabled:opacity-50";
const primary = `${button} bg-[var(--action-surface)] text-[var(--action-text)]`;
const states: Record<GenerationSession["state"], string> = {
  Ready: "Pronta para continuar", Processing: "Processando unidade", Paused: "Geração interrompida",
  Completed: "Geração concluída", Cancelled: "Sessão cancelada",
};
const coverageLabels = { Pending: "Pendente", Generated: "Processada", MissingSource: "Sem fonte narrativa", Failed: "Falha de processamento" };
const terminal = (session: GenerationSession) => session.state === "Completed" || session.state === "Cancelled";
const message = (cause: unknown) => cause instanceof Error ? cause.message : "Não foi possível concluir a operação.";

function waitUntil(at: string | null, signal: AbortSignal): Promise<void> {
  const delay = at ? Math.max(0, Date.parse(at) - Date.now()) : 0;
  if (!delay) return Promise.resolve();
  return new Promise((resolve, reject) => {
    signal.throwIfAborted();
    const finish = () => { signal.removeEventListener("abort", abort); resolve(); };
    const timer = window.setTimeout(finish, delay);
    const abort = () => { window.clearTimeout(timer); reject(signal.reason); };
    signal.addEventListener("abort", abort, { once: true });
  });
}

function candidateAt(document: Record<string, unknown>, path: string): string {
  let value: unknown = document;
  for (const part of path.replace(/^\$\.?/, "").match(/[^.[\]]+/g) ?? []) {
    if (!value || typeof value !== "object" || !Object.prototype.hasOwnProperty.call(value, part)) return "Item não encontrado nesta versão.";
    value = (value as Record<string, unknown>)[part];
  }
  if (value && typeof value === "object" && "firstKnownAtUnitSlug" in value) {
    const entity = value as Record<string, unknown>;
    value = { name: entity.name, type: entity.type, shortDescription: entity.shortDescription, firstKnownAtUnitSlug: entity.firstKnownAtUnitSlug };
  }
  return JSON.stringify(value, null, 2) ?? "Item não encontrado nesta versão.";
}

export function AdminMemoryPackGeneration({ book, disabled, disabledReason, onWorkingChange, onGenerated }: Props) {
  const base = `/api/admin/books/${book.id}/memory-pack/generations`;
  const [availability, setAvailability] = useState<GenerationAvailability | null>(null);
  const [sourcesText, setSourcesText] = useState("");
  const [parsed, setParsed] = useState<GenerationSourceParseResult | null>(null);
  const [attribution, setAttribution] = useState("");
  const [sourceUrl, setSourceUrl] = useState("");
  const [editionConfirmed, setEditionConfirmed] = useState(false);
  const [indexComplete, setIndexComplete] = useState(false);
  const [outline, setOutline] = useState<Outline | null>(null);
  const [session, setSession] = useState<GenerationSession | null>(null);
  const [result, setResult] = useState<GenerationResult | null>(null);
  const [reviewed, setReviewed] = useState(false);
  const [partialAcknowledged, setPartialAcknowledged] = useState(false);
  const [working, setWorking] = useState<Working>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [clock, setClock] = useState(() => Date.now());
  const controller = useRef<AbortController | null>(null);
  const sessionRef = useRef<GenerationSession | null>(null);
  const pendingOperation = useRef<Operation | null>(null);
  const mounted = useRef(true);
  const reviewHeading = useRef<HTMLHeadingElement>(null);
  const sourceBytes = useMemo(() => new TextEncoder().encode(sourcesText).byteLength, [sourcesText]);
  const evidence = result?.report.evidence;
  const evidenceByUnit = useMemo(() => {
    const grouped = new Map<string, GenerationEvidence[]>();
    for (const item of evidence ?? []) grouped.set(item.unitSlug, [...(grouped.get(item.unitSlug) ?? []), item]);
    return grouped;
  }, [evidence]);
  const hasContent = book.unitCount > 0 || book.entityCount > 0 || book.factCount > 0 || book.relationCount > 0 || book.eventCount > 0
    || book.readingOutlineStatus === "Published" || book.loreStatus === "Published";
  const otherSession = availability?.existingSession?.bookId !== book.id ? availability?.existingSession : null;
  const otherActive = Boolean(otherSession && !terminal(otherSession));
  const formDisabled = disabled || working !== null || !availability?.enabled || hasContent || otherActive;
  const waitingSeconds = session?.nextAllowedAt ? Math.max(0, Math.ceil((Date.parse(session.nextAllowedAt) - clock) / 1000)) : 0;

  function updateSession(updated: GenerationSession) {
    sessionRef.current = updated;
    setSession(updated);
    setClock(Date.now());
  }

  useEffect(() => {
    mounted.current = true;
    const loading = new AbortController();
    apiGet<GenerationAvailability>(`${base}/availability`, { signal: loading.signal }).then(async (available) => {
      if (loading.signal.aborted) return;
      setAvailability(available);
      if (available.existingSession?.bookId === book.id) {
        sessionRef.current = available.existingSession;
        setSession(available.existingSession);
        setNotice("Sessão recuperada. Consulte o progresso ou continue; as fontes permanecem temporariamente no servidor.");
        if (available.existingSession.completedUnits > 0) {
          const recovered = await apiGet<GenerationResult>(`${base}/${available.existingSession.runId}/result`, { signal: loading.signal });
          if (!loading.signal.aborted) setResult(recovered);
        }
      }
    }).catch((cause) => { if (!loading.signal.aborted) setError(message(cause)); });
    return () => {
      mounted.current = false;
      loading.abort();
      controller.current?.abort();
      onWorkingChange(false);
    };
  }, [base, book.id, onWorkingChange]);

  useEffect(() => {
    if (working !== "run" || !session?.nextAllowedAt) return;
    const timer = window.setInterval(() => setClock(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [working, session?.nextAllowedAt]);

  function begin(action: NonNullable<Working>): AbortController | null {
    if (controller.current) return null;
    const next = new AbortController();
    controller.current = next;
    setWorking(action);
    onWorkingChange(true);
    setError("");
    setNotice("");
    return next;
  }

  function finish(current: AbortController) {
    if (controller.current !== current) return;
    controller.current = null;
    if (mounted.current) { setWorking(null); onWorkingChange(false); }
  }

  async function checkConfiguration() {
    const current = begin("configuration");
    if (!current) return;
    try {
      const available = await apiGet<GenerationAvailability>(`${base}/availability`, { signal: current.signal });
      if (current.signal.aborted) return;
      setAvailability(available);
      setNotice(available.enabled ? "Configuração carregada. Prepare as fontes para gerar o rascunho. A chave ainda será validada pelo Groq na primeira geração." : "Disponibilidade atualizada. Confira os impedimentos abaixo.");
    } catch (cause) { if (!current.signal.aborted) setError(message(cause)); }
    finally { finish(current); }
  }

  async function parseSources() {
    if (formDisabled) return;
    const current = begin("parse");
    if (!current) return;
    try {
      setParsed(await apiWrite<GenerationSourceParseResult>(`${base}/parse`, "POST", { text: sourcesText }, { signal: current.signal }));
    } catch (cause) { if (!current.signal.aborted) setError(message(cause)); }
    finally { finish(current); }
  }

  async function consultOutline() {
    const current = begin("outline");
    if (!current) return;
    try {
      setOutline(await apiGet<Outline>(`/api/admin/books/${book.id}/reading-outline/source`, { signal: current.signal }));
    } catch (cause) {
      if (!current.signal.aborted) {
        if (cause instanceof ApiResponseError && cause.status === 404) setNotice("Esta edição não possui sumário disponível. Informe os capítulos manualmente.");
        else setError(message(cause));
      }
    } finally { finish(current); }
  }

  function useOutline() {
    if (!outline || sourcesText.trim() || outline.items.some((item) => item.level !== 1)) return;
    setSourcesText(outline.items.map((item, index) => `## Capítulo ${index + 1} — ${item.title}\n`).join("\n"));
    setParsed(null);
    setIndexComplete(false);
    setNotice("Títulos copiados para revisão. Complete os resumos e confira a divisão desta edição; nenhuma unidade foi importada.");
  }

  async function reconcile(runId: string, signal: AbortSignal) {
    const updated = await apiGet<GenerationSession>(`${base}/${runId}`, { signal });
    if (pendingOperation.current && updated.revision !== pendingOperation.current.expectedRevision) pendingOperation.current = null;
    updateSession(updated);
    return updated;
  }

  async function run(startNew: boolean) {
    if (disabled || !availability?.enabled || hasContent || otherActive) return;
    if (startNew && (!parsed?.isValid || !editionConfirmed || !attribution.trim())) return;
    if (startNew && sourceUrl.trim()) {
      try {
        const url = new URL(sourceUrl.trim());
        if (url.protocol !== "https:" || url.username || url.password) throw new Error();
      } catch { setError("Informe uma URL HTTPS da fonte narrativa, sem credenciais, ou deixe o campo vazio."); return; }
    }
    const current = begin("run");
    if (!current) return;
    if (startNew) setResult(null);
    setReviewed(false);
    setPartialAcknowledged(false);
    try {
      let progress = startNew
        ? await apiWrite<GenerationSession>(base, "POST", {
          sources: parsed!.sources, attribution: attribution.trim(), sourceUrl: sourceUrl.trim() || null,
          editionConfirmed, indexComplete,
        }, { signal: current.signal })
        : await reconcile(sessionRef.current!.runId, current.signal);
      updateSession(progress);
      while (!terminal(progress) && progress.state !== "Processing") {
        await waitUntil(progress.nextAllowedAt, current.signal);
        current.signal.throwIfAborted();
        const operation = pendingOperation.current ?? { operationId: crypto.randomUUID(), expectedRevision: progress.revision };
        pendingOperation.current = operation;
        progress = await apiWrite<GenerationSession>(`${base}/${progress.runId}/next`, "POST", operation, { signal: current.signal });
        pendingOperation.current = null;
        updateSession(progress);
        const latest = await apiGet<GenerationResult>(`${base}/${progress.runId}/result`, { signal: current.signal });
        setResult(latest);
        setReviewed(false);
        setPartialAcknowledged(false);
        progress = latest.status;
        updateSession(progress);
      }
      if (progress.state === "Completed") {
        setNotice("Geração concluída. Revise a cobertura, os candidatos e suas evidências antes de validar o pacote.");
        requestAnimationFrame(() => reviewHeading.current?.focus());
      } else if (progress.state === "Processing") setNotice("Há uma unidade em processamento. Consulte o progresso antes de continuar.");
    } catch (cause) {
      if (!current.signal.aborted) {
        setError(`${message(cause)} A retomada é manual; o último resultado concluído foi preservado.`);
        if (cause instanceof ApiResponseError) pendingOperation.current = null;
        if (sessionRef.current) {
          try { await reconcile(sessionRef.current.runId, current.signal); }
          catch { setNotice("Não foi possível atualizar o progresso. Consulte a sessão antes de tentar novamente."); }
        }
      }
    } finally { finish(current); }
  }

  async function refresh() {
    if (!sessionRef.current) return;
    const current = begin("refresh");
    if (!current) return;
    try { await reconcile(sessionRef.current.runId, current.signal); }
    catch (cause) { if (!current.signal.aborted) setError(message(cause)); }
    finally { finish(current); }
  }

  async function reviewResult() {
    if (!sessionRef.current) return;
    const current = begin("result");
    if (!current) return;
    try {
      const generated = await apiGet<GenerationResult>(`${base}/${sessionRef.current.runId}/result`, { signal: current.signal });
      updateSession(generated.status);
      setResult(generated);
      setReviewed(false);
      setPartialAcknowledged(false);
      requestAnimationFrame(() => reviewHeading.current?.focus());
    } catch (cause) { if (!current.signal.aborted) setError(message(cause)); }
    finally { finish(current); }
  }

  async function cancel() {
    controller.current?.abort();
    controller.current = null;
    const current = begin("cancel");
    if (!current) return;
    try {
      let active = sessionRef.current;
      if (!active) {
        const available = await apiGet<GenerationAvailability>(`${base}/availability`, { signal: current.signal });
        active = available.existingSession?.bookId === book.id ? available.existingSession : null;
      }
      if (active && !terminal(active)) updateSession(await apiWrite<GenerationSession>(`${base}/${active.runId}/cancel`, "POST", undefined, { signal: current.signal }));
      pendingOperation.current = null;
      setNotice("Geração encerrada. O resultado já concluído pode ser revisado e baixado.");
    } catch (cause) { if (!current.signal.aborted) setError(message(cause)); }
    finally { finish(current); }
  }

  function sendToTransfer() {
    if (!result || !reviewed || sessionRef.current?.state === "Processing" || (result.report.partial && !partialAcknowledged)) return;
    onGenerated({
      id: crypto.randomUUID(), json: result.documentJson, originalJson: result.documentJson,
      isPartial: result.report.partial, reportHash: result.report.packHash, reviewed, partialAcknowledged,
    });
    setNotice("Resultado enviado para validação abaixo. A importação continuará após sua confirmação.");
  }

  return (
    <section aria-labelledby="generation-title" className="mt-8 min-w-0 max-w-4xl rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5">
      <span className="section-index">GERAÇÃO ASSISTIDA</span>
      <h2 id="generation-title" className="mt-2 text-2xl font-semibold">Gerar pacote com IA</h2>
      <p className="mt-2 text-sm text-[var(--ink-soft)]">O conteúdo é extraído em ordem, com evidências por capítulo. A revisão editorial e dos pontos de revelação continua sendo sua responsabilidade.</p>
      <p className="mt-2 text-xs text-[var(--ink-soft)]">Sessão temporária: reinício ou expiração elimina os artefatos no servidor. Baixe o JSON e o relatório para conservá-los.</p>
      {!availability && !error && <p role="status" className="mt-4 text-sm">Verificando disponibilidade…</p>}
      {availability?.reason && <p role="status" className="mt-4 rounded-lg border border-[var(--line)] p-3 text-sm">{availability.reason}</p>}
      {availability?.configuration && !availability.configuration.ready && <div className="mt-4 rounded-xl border border-[var(--line)] bg-[var(--paper)] p-4">
        <h3 className="font-semibold">Configurar a geração assistida</h3>
        <ul className="mt-3 list-disc space-y-2 pl-5 text-sm">{availability.configuration.issues.map((issue) => <li key={`${issue.code}:${issue.setting}`} className="break-words">{issue.message}</li>)}</ul>
        {availability.configuration.localSetup ? <>
          <p className="mt-3 text-sm">No PowerShell, na pasta do projeto, execute:</p>
          <pre className="mt-2 overflow-x-auto rounded-lg border border-[var(--line)] p-3 text-sm"><code>{".\\scripts\\configure-generation.ps1"}</code></pre>
          <p className="mt-3 text-sm">O comando pede a chave com entrada oculta e salva a configuração no seu perfil de desenvolvimento, fora do Git. Depois, reinicie a API na porta 5100 e verifique novamente. Variáveis antigas no terminal têm prioridade sobre essa configuração.</p>
        </> : <p className="mt-3 text-sm">No Render, configure as variáveis acima em Environment do serviço da API e aplique o deploy. Uma chave definida somente no seu computador não configura o servidor online.</p>}
        <p className="mt-3 text-sm text-[var(--ink-soft)]">Use o Groq Free, sem upgrade pago. A chave pertence ao backend: não a cole neste formulário, no JSON ou no chat. Esta verificação não chama o provedor.</p>
      </div>}
      {(availability && !availability.enabled || !availability && error) && <button type="button" disabled={disabled || working !== null} onClick={() => void checkConfiguration()} className={`${button} mt-4`}>{working === "configuration" ? "Verificando…" : "Verificar configuração novamente"}</button>}
      {disabledReason && <p className="mt-4 text-sm">{disabledReason}</p>}
      {otherSession && <p className="mt-4 rounded-lg border border-[var(--line)] p-3 text-sm">Você tem uma sessão {otherActive ? "em andamento" : "concluída ou cancelada"} em outro livro. <Link className="underline" href={`/admin/books/${otherSession.bookId}`}>Abrir essa sessão</Link>{otherActive ? " para retomar ou cancelar antes de iniciar outra." : "."}</p>}
      {error && <p role="alert" className="form-alert mt-4 rounded-lg border p-3 text-sm">{error}</p>}
      {notice && <p role="status" className="mt-4 text-sm">{notice}</p>}

      {!session && (
        <div className="mt-5 grid min-w-0 gap-4">
          {book.externalProvider === "OpenLibrary" && book.externalId?.toUpperCase().endsWith("M") && (
            <details className="rounded-xl border border-[var(--line)] p-3">
              <summary className="min-h-11 cursor-pointer py-2 text-sm font-semibold">Consultar títulos na Open Library</summary>
              <p className="mt-2 text-sm text-[var(--ink-soft)]">O sumário opcional da edição ajuda a conferir a sequência; os resumos narrativos devem ser fornecidos por você.</p>
              <button type="button" disabled={formDisabled} onClick={() => void consultOutline()} className={`${button} mt-3`}>{working === "outline" ? "Consultando…" : "Buscar sumário desta edição"}</button>
              {outline && <div className="mt-3">
                <p className="text-sm">{outline.items.length} títulos encontrados · {outline.attribution}</p>
                <ol className="mt-3 max-h-56 overflow-auto rounded-lg border border-[var(--line)] p-3 text-sm">
                  {outline.items.map((item, index) => <li key={`${item.title}-${index}`} className="break-words" style={{ marginLeft: `${Math.max(0, item.level - 1) * 12}px` }}>{index + 1}. {item.title}</li>)}
                </ol>
                <button type="button" className={`${button} mt-3`} disabled={formDisabled || Boolean(sourcesText.trim()) || outline.items.some((item) => item.level !== 1)} onClick={useOutline}>Usar títulos no formulário vazio</button>
                {outline.items.some((item) => item.level !== 1) && <p className="mt-2 text-sm text-[var(--ink-soft)]">Este sumário inclui partes ou seções. Confira a divisão e informe os capítulos no lote; a hierarquia não será convertida em capítulos automaticamente.</p>}
                {sourcesText.trim() && <p className="mt-2 text-xs text-[var(--ink-soft)]">Seu texto já contém fontes. Preserve-o e confira o sumário acima.</p>}
              </div>}
            </details>
          )}
          <label className="grid gap-2 text-sm" htmlFor="generation-sources">Fontes por capítulo</label>
          <textarea id="generation-sources" rows={10} disabled={formDisabled} aria-describedby="generation-source-help" value={sourcesText}
            onChange={(event) => { setSourcesText(event.target.value); setParsed(null); setIndexComplete(false); }}
            placeholder={"## Capítulo 1 — A chegada\nResumo narrativo do capítulo.\n\n## Capítulo 2 — A travessia\nResumo narrativo do capítulo."}
            className="w-full min-w-0 rounded-lg border p-3 text-sm" />
          <p id="generation-source-help" className="text-xs text-[var(--ink-soft)]">Use cabeçalhos consecutivos desde o capítulo 1. Para uma fonte ausente, conserve o cabeçalho com resumo vazio. Até {availability?.maxUnits ?? 64} unidades e {Math.floor((availability?.maxSourceBytes ?? 524288) / 1024)} KiB; {Math.ceil(sourceBytes / 1024)} KiB informados. Fontes devem preservar o conhecimento daquele capítulo.</p>
          <button type="button" disabled={formDisabled || !sourcesText.trim() || sourceBytes > (availability?.maxSourceBytes ?? 524288)} onClick={() => void parseSources()} className={`${button} justify-self-start`}>{working === "parse" ? "Conferindo…" : "Conferir capítulos e lacunas"}</button>
          {sourceBytes > (availability?.maxSourceBytes ?? 524288) && <p role="alert" className="form-alert rounded-lg border p-3 text-sm">As fontes ultrapassam o limite. Revise o lote; o texto não será cortado automaticamente.</p>}
          {parsed && <div className="rounded-xl border border-[var(--line)] p-4">
            <h3 className="font-semibold">{parsed.sources.length} unidades identificadas</h3>
            <ol className="mt-3 max-h-64 overflow-auto text-sm">
              {parsed.sources.map((source, index) => <li key={`${source.title}-${index}`} className="py-1 break-words">{index + 1}. {source.title}{!source.summary.trim() ? " · sem fonte narrativa" : ""}</li>)}
            </ol>
            {parsed.issues.length > 0 && <ul className="mt-3 grid gap-2 text-sm">{parsed.issues.map((issue, index) => <li key={`${issue.path}-${index}`}><strong>{issue.isBlocking ? "Corrigir" : "Atenção"}</strong>: {issue.message}</li>)}</ul>}
          </div>}
          <label className="grid gap-2 text-sm">Atribuição das fontes
            <input maxLength={300} disabled={formDisabled} value={attribution} onChange={(event) => setAttribution(event.target.value)} placeholder="Ex.: resumos próprios da edição revisada" className="min-h-11 rounded-lg border px-3 py-2" />
          </label>
          <label className="grid gap-2 text-sm">URL da fonte narrativa (opcional)
            <input type="url" maxLength={2000} disabled={formDisabled} value={sourceUrl} onChange={(event) => setSourceUrl(event.target.value)} placeholder="https://…" className="min-h-11 rounded-lg border px-3 py-2" />
          </label>
          <label className="flex min-h-11 items-start gap-3 text-sm"><input type="checkbox" disabled={formDisabled} checked={editionConfirmed} onChange={(event) => setEditionConfirmed(event.target.checked)} className="mt-1 shrink-0" /><span>Conferi que as fontes pertencem a <strong>{book.title}</strong>, de {book.author}, e a esta edição.</span></label>
          <label className="flex min-h-11 items-start gap-3 text-sm"><input type="checkbox" disabled={formDisabled} checked={indexComplete} onChange={(event) => setIndexComplete(event.target.checked)} className="mt-1 shrink-0" /><span>Conferi que o índice está completo. Se esta confirmação ficar vazia, o resultado será identificado como parcial.</span></label>
          <p className="text-sm text-[var(--ink-soft)]">Os resumos serão enviados ao Groq para extração. Confira o conteúdo das fontes antes de iniciar.</p>
          <button type="button" disabled={formDisabled || !parsed?.isValid || !editionConfirmed || !attribution.trim()} onClick={() => void run(true)} className={`${primary} justify-self-start`}>Gerar rascunho</button>
        </div>
      )}

      {(session || working === "run") && <div className="mt-5 rounded-xl border border-[var(--line)] bg-[var(--paper)] p-4">
        <h3 className="text-lg font-semibold" role="status">{session ? states[session.state] : "Iniciando sessão…"}</h3>
        {session && <>
          <p className="mt-2 text-sm">{session.completedUnits} de {session.totalUnits} unidades processadas · {session.calls} chamadas · {session.tokens.toLocaleString("pt-BR")} tokens contabilizados{session.estimatedTokens > 0 ? " (inclui estimativas)" : ""}</p>
          <progress className="mt-3 w-full accent-[var(--olive)]" value={session.completedUnits} max={session.totalUnits} aria-label="Unidades processadas" />
          {working === "run" && waitingSeconds > 0 && <p className="mt-2 text-sm text-[var(--ink-soft)]">Próxima unidade em {waitingSeconds}s, respeitando o intervalo do serviço.</p>}
          <ol className="mt-3 max-h-56 overflow-auto text-sm">{session.coverage.map((unit, index) => <li key={unit.unitSlug} className="py-1 break-words">{index + 1}. {unit.title} · {coverageLabels[unit.status]}</li>)}</ol>
          {session.diagnostics.length > 0 && <details className="mt-3"><summary className="min-h-11 cursor-pointer py-2 text-sm font-semibold">Alertas da geração ({session.diagnostics.length})</summary><ul className="grid gap-2 text-sm">{session.diagnostics.map((diagnostic, index) => <li key={index} className="break-words">{diagnostic}</li>)}</ul></details>}
        </>}
        <div className="mt-4 flex flex-wrap gap-2">
          {session && !terminal(session) && <button type="button" disabled={formDisabled || session.state === "Processing"} className={primary} onClick={() => void run(false)}>Continuar geração</button>}
          {session && <button type="button" disabled={working !== null} className={button} onClick={() => void refresh()}>Consultar progresso</button>}
          {session && <button type="button" disabled={working !== null || session.state === "Processing"} className={button} onClick={() => void reviewResult()}>Revisar resultado {session.state === "Completed" ? "gerado" : "parcial"}</button>}
          {(working === "run" || session && !terminal(session)) && <button type="button" disabled={working === "cancel"} className={button} onClick={() => void cancel()}>{working === "cancel" ? "Cancelando…" : "Cancelar geração"}</button>}
          {session && terminal(session) && <button type="button" disabled={formDisabled} className={button} onClick={() => { setSession(null); sessionRef.current = null; setResult(null); setParsed(null); pendingOperation.current = null; }}>Preparar nova geração</button>}
        </div>
      </div>}

      {result && <div className="mt-6 min-w-0">
        <h3 ref={reviewHeading} tabIndex={-1} className="text-xl font-semibold">{result.report.partial ? "Revisar resultado parcial" : "Revisar resultado gerado"}</h3>
        {result.report.partial && <p className="mt-3 rounded-lg border border-[var(--line)] bg-[var(--surface-tint)] p-3 text-sm">Este pacote está incompleto: há unidades sem processamento, sem fontes ou um índice não confirmado. Nenhum conteúdo ausente foi inventado.</p>}
        <p className="mt-3 text-sm text-[var(--ink-soft)]">Relatório da versão original. Confira os candidatos, citações e pontos de revelação; validação estrutural não comprova a correção editorial.</p>
        <p className="mt-2 break-words text-xs text-[var(--ink-soft)]">Fonte: {result.report.sourceAttribution || "Não informada"} · {result.report.provider} / {result.report.model}</p>
        <p className="mt-2 break-all text-xs text-[var(--ink-soft)]">Hash do JSON original: {result.report.packHash}</p>
        <div className="mt-4 grid gap-3">
          {result.report.units.map((unit, index) => <details key={unit.unitSlug} className="min-w-0 rounded-xl border border-[var(--line)] p-4">
            <summary className="min-h-11 cursor-pointer py-2 text-sm font-semibold">{index + 1}. {unit.title} · {coverageLabels[unit.status]} · {evidenceByUnit.get(unit.unitSlug)?.length ?? 0} evidências</summary>
            {(evidenceByUnit.get(unit.unitSlug) ?? []).map((item, itemIndex) => <article key={`${item.itemId}-${item.paragraphId}-${itemIndex}`} className="mt-3 min-w-0 rounded-lg border border-[var(--line)] bg-[var(--paper)] p-3">
              <p className="break-all text-xs text-[var(--ink-soft)]">{item.jsonPath} · parágrafo {item.paragraphId}</p>
              <pre className="mt-2 max-h-48 overflow-auto whitespace-pre-wrap break-words text-xs">{candidateAt(result.document, item.jsonPath)}</pre>
              <blockquote className="mt-3 border-l-2 border-[var(--olive)] pl-3 text-sm whitespace-pre-wrap break-words">{item.quote}</blockquote>
              <p className="mt-2 text-sm text-[var(--ink-soft)] break-words">{item.justification}</p>
            </article>)}
            {!evidenceByUnit.get(unit.unitSlug)?.length && <p className="mt-2 text-sm text-[var(--ink-soft)]">Nenhum candidato com evidência foi aceito nesta unidade. Confira as fontes e lacunas.</p>}
          </details>)}
        </div>
        {result.report.diagnostics.length > 0 && <details className="mt-4"><summary className="min-h-11 cursor-pointer py-2 text-sm font-semibold">Diagnósticos completos ({result.report.diagnostics.length})</summary><ul className="grid gap-2 text-sm">{result.report.diagnostics.map((diagnostic, index) => <li key={index} className="break-words">{diagnostic}</li>)}</ul></details>}
        {!result.validation.isValid && <div className="form-alert mt-4 rounded-lg border p-3 text-sm"><strong>O resultado precisa de correções antes de importar.</strong><ul className="mt-2 grid gap-1">{result.validation.issues.map((issue, index) => <li key={`${issue.path}-${index}`} className="break-words">{issue.path}: {issue.message}</li>)}</ul></div>}
        <div className="mt-4 flex flex-wrap gap-2">
          <button type="button" className={button} onClick={() => downloadJson(`mnemora-${book.id}-gerado-v1.json`, result.documentJson)}>Baixar JSON original</button>
          <button type="button" className={button} onClick={() => downloadJson(`mnemora-${book.id}-relatorio.json`, JSON.stringify(result.report, null, 2))}>Baixar relatório original</button>
        </div>
        <div className="mt-5 grid gap-3">
          <label className="flex min-h-11 items-start gap-3 text-sm"><input type="checkbox" checked={reviewed} disabled={working !== null || disabled} onChange={(event) => setReviewed(event.target.checked)} className="mt-1 shrink-0" /><span>Revisei os candidatos, as evidências e os pontos de revelação desta versão.</span></label>
          {result.report.partial && <label className="flex min-h-11 items-start gap-3 text-sm"><input type="checkbox" checked={partialAcknowledged} disabled={working !== null || disabled} onChange={(event) => setPartialAcknowledged(event.target.checked)} className="mt-1 shrink-0" /><span>Reconheço que este resultado é parcial e revisei suas lacunas antes de seguir para validação.</span></label>}
          <button type="button" className={`${primary} justify-self-start`} disabled={working !== null || disabled || session?.state === "Processing" || !reviewed || result.report.partial && !partialAcknowledged} onClick={sendToTransfer}>Enviar para validação abaixo</button>
        </div>
      </div>}
    </section>
  );
}
