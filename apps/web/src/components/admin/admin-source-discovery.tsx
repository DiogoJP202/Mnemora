"use client";

import { useEffect, useRef, useState } from "react";
import { apiWrite, downloadJson } from "@/lib/api";
import type { AdminBook } from "@/lib/admin-types";

type Discovery = {
  book: { bookId: string; title: string; author: string };
  retrievedAt: string;
  outlineStatus: "Candidate" | "Missing" | "Conflict" | "NotApplicable" | "Failed";
  outline: { title: string; level: number; page: number | null }[];
  narrativeStatus: "NotVerified";
  sources: { provider: string; externalId: string; title: string; url: string;
    kind: string; match: string; language: string | null; attribution: string;
    retrievedAt: string; responseHash: string; requiresReview: boolean }[];
  diagnostics: string[];
};

const outlineLabels: Record<Discovery["outlineStatus"], string> = {
  Candidate: "Sumário candidato para revisão", Missing: "Sumário não encontrado nesta edição",
  Conflict: "Conflito na identificação da edição", NotApplicable: "Edição sem consulta de sumário compatível",
  Failed: "Consulta de sumário indisponível",
};
const matchLabels: Record<string, string> = {
  EditionIdentifier: "Identificador da edição", EditionIsbn: "ISBN correspondente",
  WorkIdentifier: "Identificador da obra; tradução a conferir", UnverifiedWork: "Obra candidata sem correspondência confirmada",
};
const kindLabels: Record<string, string> = {
  EditionMetadata: "Metadados da edição", WorkReference: "Referência bibliográfica", GeneralArticle: "Artigo geral; pode conter spoilers",
};

export function AdminSourceDiscovery({ book, disabled, onWorkingChange }: {
  book: AdminBook; disabled: boolean; onWorkingChange: (working: boolean) => void;
}) {
  const [result, setResult] = useState<Discovery | null>(null);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState("");
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), []);

  async function discover() {
    if (request.current || disabled) return;
    const controller = new AbortController();
    request.current = controller;
    setWorking(true); onWorkingChange(true); setError("");
    try {
      const value = await apiWrite<Discovery>(`/api/admin/books/${book.id}/source-discovery`, "POST", undefined, { signal: controller.signal });
      if (!controller.signal.aborted) setResult(value);
    } catch (cause) {
      if (!controller.signal.aborted) setError(cause instanceof Error ? cause.message : "Não foi possível consultar fontes.");
    } finally {
      if (request.current === controller) request.current = null;
      if (!controller.signal.aborted) { setWorking(false); onWorkingChange(false); }
    }
  }

  return <section aria-labelledby="source-discovery-title" className="mt-8 min-w-0 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5">
    <span className="section-index">DESCOBERTA DE FONTES</span>
    <h2 id="source-discovery-title" className="mt-2 text-2xl font-semibold">Encontrar referências públicas</h2>
    <p className="mt-3 text-sm text-[var(--ink-soft)]">Consulte título e identificadores desta edição na Open Library e no Wikidata. Veja sumário candidato e links de referência para conferir a obra. Esta consulta não usa IA e não cria capítulos ou conhecimento.</p>
    <button type="button" disabled={disabled || working} onClick={() => void discover()} className="mt-4 min-h-11 rounded-lg bg-[var(--action-surface)] px-4 py-2 text-sm text-[var(--action-text)] disabled:opacity-50">{working ? "Consultando fontes…" : "Descobrir fontes públicas"}</button>
    {disabled && !working && <p className="mt-3 text-sm">Conclua a operação em andamento e salve os metadados para consultar esta edição.</p>}
    {working && <p role="status" className="mt-3 text-sm">Conferindo a edição e até três candidatos bibliográficos. As fontes podem responder parcialmente.</p>}
    {error && <p role="alert" className="form-alert mt-4 rounded-lg border p-3 text-sm">{error}</p>}
    {result && <div className="mt-5 min-w-0 space-y-4">
      <p className="break-words text-sm">Identidade consultada: <strong>{result.book.title}</strong> · {result.book.author}</p>
      <p role="status" className="font-semibold">{outlineLabels[result.outlineStatus]}{result.outline.length > 0 ? ` · ${result.outline.length} itens` : ""}. Fontes narrativas por capítulo: ainda não verificadas.</p>
      <p className="text-sm text-[var(--ink-soft)]">Consulta de {new Date(result.retrievedAt).toLocaleString("pt-BR")}. Resultados públicos podem ser reutilizados por até uma hora. Referências da obra não confirmam automaticamente edição, tradução ou ponto de revelação.</p>
      <ul className="list-disc space-y-2 pl-5 text-sm">{result.diagnostics.map((diagnostic, index) => <li key={index}>{diagnostic}</li>)}</ul>
      {result.outline.length > 0 && <details className="rounded-lg border border-[var(--line)] p-3"><summary className="min-h-11 cursor-pointer py-2 font-semibold">Conferir itens do sumário candidato</summary><ol className="list-decimal space-y-2 pl-6 text-sm">{result.outline.map((item, index) => <li key={index} className="break-words">{item.title}{item.page ? ` · p. ${item.page}` : ""}</li>)}</ol><p className="mt-3 text-sm">Para cadastrar unidades, use a consulta e importação revisada de sumário no editor de unidades de leitura.</p></details>}
      <div className="grid min-w-0 gap-3">{result.sources.map((source) => <article key={`${source.provider}:${source.externalId}:${source.language}`} className="min-w-0 rounded-xl border border-[var(--line)] p-4">
        <p className="text-xs text-[var(--ink-soft)]">{source.provider} · {kindLabels[source.kind] ?? source.kind}</p>
        <h3 className="mt-1 break-words text-lg font-semibold"><a href={source.url} target="_blank" rel="noopener noreferrer" className="underline underline-offset-4">{source.title} ↗</a></h3>
        <p className="mt-2 text-sm">{matchLabels[source.match] ?? "Correspondência a conferir"} · revisão necessária{source.language ? ` · ${source.language}` : ""}</p>
        <p className="mt-2 break-words text-xs text-[var(--ink-soft)]">{source.attribution}</p>
        <details className="mt-3 text-xs"><summary className="min-h-11 cursor-pointer py-2">Procedência da descoberta</summary><p className="break-all">{source.url}</p><p className="mt-2 break-all">SHA-256 da resposta de descoberta: {source.responseHash}</p><p className="mt-2">{new Date(source.retrievedAt).toLocaleString("pt-BR")}. Links de artigos foram obtidos no Wikidata; o texto dessas páginas não foi coletado.</p></details>
      </article>)}</div>
      <button type="button" onClick={() => downloadJson(`mnemora-${book.id}-fontes.json`, JSON.stringify(result, null, 2))} className="min-h-11 rounded-lg border border-[var(--line)] px-4 py-2 text-sm">Baixar levantamento de fontes</button>
      <p className="text-sm text-[var(--ink-soft)]">Este relatório é material de pesquisa, sem conteúdo para importação. O gerador continua recebendo apenas fontes narrativas delimitadas por capítulo, fornecidas e revisadas pelo Admin.</p>
    </div>}
  </section>;
}
