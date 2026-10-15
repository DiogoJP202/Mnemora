"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { apiGet, apiWrite } from "@/lib/api";
import { metadataRows, languageName, preferredIsbn } from "@/lib/book-edition";
import type { SearchBook } from "@/lib/reader-types";
import type { AdminBook } from "@/lib/admin-types";

const button = "min-h-11 rounded-lg border border-[var(--line)] px-4 py-2 text-sm disabled:opacity-50";

export function AdminBookImport() {
  const router = useRouter();
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SearchBook[] | null>(null);
  const [selected, setSelected] = useState<SearchBook | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [working, setWorking] = useState<"search" | "import" | null>(null);
  const [error, setError] = useState("");
  const controller = useRef<AbortController | null>(null);
  const reviewHeading = useRef<HTMLHeadingElement>(null);
  const reviewTrigger = useRef<HTMLButtonElement | null>(null);
  const isBusy = working !== null;

  useEffect(() => () => controller.current?.abort(), []);

  async function search(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (controller.current || !query.trim()) return;
    const request = new AbortController();
    controller.current = request;
    setWorking("search");
    setError("");
    setResults(null);
    setSelected(null);
    setConfirmed(false);
    try {
      const matches = await apiGet<SearchBook[]>(`/api/admin/books/search?q=${encodeURIComponent(query.trim())}`, { signal: request.signal });
      if (!request.signal.aborted) setResults(matches);
    } catch (cause) {
      if (!request.signal.aborted) setError(cause instanceof Error ? cause.message : "Não foi possível buscar livros.");
    } finally {
      if (!request.signal.aborted) setWorking(null);
      if (controller.current === request) controller.current = null;
    }
  }

  async function importEdition() {
    if (!selected?.externalProvider || !selected.externalId || !confirmed || controller.current) return;
    const request = new AbortController();
    controller.current = request;
    setWorking("import");
    setError("");
    try {
      const book = await apiWrite<AdminBook>("/api/admin/books/external", "POST", {
        externalProvider: selected.externalProvider, externalId: selected.externalId,
      }, { signal: request.signal });
      if (!request.signal.aborted) router.push(`/admin/books/${book.id}`);
    } catch (cause) {
      if (!request.signal.aborted) setError(cause instanceof Error ? cause.message : "Não foi possível importar a edição.");
    } finally {
      if (!request.signal.aborted) setWorking(null);
      if (controller.current === request) controller.current = null;
    }
  }

  return (
    <section aria-labelledby="admin-book-import-title" className="mt-8 min-w-0 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5">
      <span className="section-index">COMECE PELO LIVRO</span>
      <h2 id="admin-book-import-title" className="mt-2 text-2xl font-semibold">Buscar livro para preparar</h2>
      <p className="mt-2 text-sm text-[var(--ink-soft)]">Encontre a edição no catálogo e importe os dados bibliográficos disponíveis. Continue a preparação do índice e do conhecimento na página do livro.</p>
      <form onSubmit={search} className="mt-5 flex min-w-0 flex-wrap items-end gap-3">
        <label className="grid min-w-0 flex-1 basis-60 gap-2 text-sm">Buscar por título, autor ou ISBN
          <input type="search" required maxLength={100} disabled={isBusy} value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Ex.: Harry Potter e a Pedra Filosofal" className="min-h-11 min-w-0 rounded-lg border px-3 py-2" />
        </label>
        <button className={`${button} bg-[var(--action-surface)] text-[var(--action-text)]`} disabled={isBusy || !query.trim()}>{working === "search" ? "Buscando…" : "Buscar livros"}</button>
      </form>
      {error && <p role="alert" className="form-alert mt-4 rounded-lg border p-3 text-sm">{error}</p>}
      {working === "search" && <p role="status" className="mt-4 text-sm">Consultando o catálogo e os provedores configurados…</p>}
      {results && <div className="mt-5">
        <p role="status" className="text-sm text-[var(--ink-soft)]">{results.length ? `${results.length} edições ou livros encontrados. Confira idioma e ISBN antes de importar.` : "Nenhum livro encontrado. Tente outro título ou ISBN; o cadastro manual continua disponível abaixo."}</p>
        <div className="mt-3 grid min-w-0 gap-3">
          {results.map((book) => <article key={book.id ?? `${book.externalProvider}:${book.externalId}`} className="min-w-0 rounded-xl border border-[var(--line)] p-4">
            <p className="text-xs text-[var(--ink-soft)]">{book.id ? "Já está no catálogo" : book.externalProvider === "OpenLibrary" ? "Open Library" : book.externalProvider === "GoogleBooks" ? "Google Books" : "Catálogo externo"}</p>
            <h3 className="mt-1 break-words text-lg font-semibold">{book.title}</h3>
            {book.subtitle && <p className="mt-1 break-words text-sm">{book.subtitle}</p>}
            <p className="mt-1 break-words text-sm text-[var(--ink-soft)]">{book.author}</p>
            <p className="mt-2 break-words text-sm">{[book.publisher, book.publishedDate?.slice(0, 4), languageName(book.language), preferredIsbn(book) ? `ISBN ${preferredIsbn(book)}` : null].filter(Boolean).join(" · ")}</p>
            {book.id ? <Link href={`/admin/books/${book.id}`} className={`${button} mt-3 inline-flex items-center`}>Abrir livro</Link>
              : <button type="button" disabled={isBusy} onClick={(event) => { reviewTrigger.current = event.currentTarget; setSelected(book); setConfirmed(false); setError(""); requestAnimationFrame(() => reviewHeading.current?.focus()); }} className={`${button} mt-3`}>Revisar edição de {book.title}</button>}
          </article>)}
        </div>
      </div>}
      {selected && <section aria-labelledby="admin-edition-review-title" className="mt-5 min-w-0 rounded-xl border border-[var(--olive)] bg-[var(--paper)] p-4">
        <h3 id="admin-edition-review-title" ref={reviewHeading} tabIndex={-1} className="text-xl font-semibold">Conferir edição antes de importar</h3>
        <dl className="admin-edition-grid mt-4">{metadataRows(selected).map((row) => <div key={row.label}><dt>{row.label}</dt><dd className="break-words">{row.value}</dd></div>)}</dl>
        <p className="mt-4 text-sm text-[var(--ink-soft)]">A importação preenche o cadastro bibliográfico. Personagens, capítulos e revelações dependem das fontes e da preparação editorial.</p>
        <label className="mt-4 flex min-h-11 items-start gap-3 text-sm"><input type="checkbox" disabled={isBusy} checked={confirmed} onChange={(event) => setConfirmed(event.target.checked)} className="mt-1 shrink-0" /><span>Conferi o título, a autoria, o idioma e os dados disponíveis desta edição.</span></label>
        <div className="mt-4 flex flex-wrap gap-2">
          <button type="button" disabled={isBusy} onClick={() => { setSelected(null); setConfirmed(false); reviewTrigger.current?.focus(); }} className={button}>Cancelar revisão</button>
          <button type="button" disabled={isBusy || !confirmed} onClick={() => void importEdition()} className={`${button} bg-[var(--action-surface)] text-[var(--action-text)]`}>{working === "import" ? "Importando…" : "Importar e abrir livro"}</button>
        </div>
      </section>}
    </section>
  );
}
