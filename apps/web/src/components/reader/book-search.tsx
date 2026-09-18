"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { apiGet, apiWrite } from "@/lib/api";
import { BookCover } from "@/components/reader/book-cover";
import type { Book, LibraryBook, SearchBook } from "@/lib/reader-types";

export function BookSearch() {
  const router = useRouter();
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SearchBook[] | null>(null);
  const [searched, setSearched] = useState(false);
  const [searching, setSearching] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [libraryBookIds, setLibraryBookIds] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [manualOpen, setManualOpen] = useState(false);
  const [manualTitle, setManualTitle] = useState("");
  const [manualAuthor, setManualAuthor] = useState("");
  const [manualIsbn, setManualIsbn] = useState("");
  const [manualError, setManualError] = useState<string | null>(null);
  const [libraryWarning, setLibraryWarning] = useState<string | null>(null);
  const manualTitleRef = useRef<HTMLInputElement>(null);
  const searchSequence = useRef(0);

  useEffect(() => {
    let active = true;
    Promise.allSettled([
      apiGet<Book[]>("/api/books"),
      apiGet<LibraryBook[]>("/api/library"),
    ]).then(([catalogResult, libraryResult]) => {
      if (!active) return;
      if (searchSequence.current === 0) {
        if (catalogResult.status === "fulfilled") {
          setResults(catalogResult.value.map((book) => ({
            id: book.id,
            externalId: null,
            externalProvider: null,
            source: "local" as const,
            title: book.title,
            author: book.author,
            coverUrl: book.coverUrl,
            memoryPackAvailable: book.memoryPackAvailable,
          })));
        } else {
          setError(catalogResult.reason instanceof Error
            ? catalogResult.reason.message
            : "Não foi possível carregar o catálogo.");
        }
      }
      if (libraryResult.status === "fulfilled") {
        setLibraryBookIds(new Set(libraryResult.value.map((item) => item.book.id)));
      } else {
        setLibraryWarning("Não foi possível conferir quais livros já estão na sua estante.");
      }
    });
    return () => { active = false; };
  }, []);

  useEffect(() => {
    if (manualOpen) manualTitleRef.current?.focus();
  }, [manualOpen]);

  async function search(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const term = query.trim();
    if (!term || searching) return;
    const sequence = ++searchSequence.current;
    setSearched(true);
    setSearching(true);
    setResults(null);
    setError(null);
    try {
      const found = await apiGet<SearchBook[]>(`/api/books/search?q=${encodeURIComponent(term)}`);
      if (sequence === searchSequence.current) setResults(found);
    } catch (cause) {
      if (sequence === searchSequence.current) {
        setError(cause instanceof Error ? cause.message : "Não foi possível fazer a busca.");
      }
    } finally {
      if (sequence === searchSequence.current) setSearching(false);
    }
  }

  async function add(book: SearchBook) {
    const key = book.id ?? `${book.externalProvider}:${book.externalId}`;
    if (!key) return;
    setBusy(key);
    setError(null);
    try {
      const libraryBook = book.source === "external"
        ? await apiWrite<LibraryBook>("/api/library/external", "POST", {
            externalProvider: book.externalProvider,
            externalId: book.externalId,
          })
        : await apiWrite<LibraryBook>(`/api/library/${book.id}`, "POST");
      setLibraryBookIds((current) => new Set(current).add(libraryBook.book.id));
      router.push(`/app/books/${libraryBook.book.id}`);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível adicionar o livro.");
      setBusy(null);
    }
  }

  async function addManual(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const title = manualTitle.trim();
    const author = manualAuthor.trim();
    const isbn = manualIsbn.trim();
    if (!title) {
      setManualError("Informe o título do livro.");
      return;
    }
    setBusy("manual");
    setManualError(null);
    try {
      const libraryBook = await apiWrite<LibraryBook>("/api/library/manual", "POST", {
        title,
        author: author || null,
        isbn: isbn || null,
      });
      setLibraryBookIds((current) => new Set(current).add(libraryBook.book.id));
      router.push(`/app/books/${libraryBook.book.id}`);
    } catch (cause) {
      setManualError(cause instanceof Error ? cause.message : "Não foi possível adicionar este livro.");
      setBusy(null);
    }
  }

  const resultMessage = error
    ? error
    : !results
      ? "Procurando histórias."
    : results.length === 0
      ? "Nenhum livro encontrado."
      : `${results.length} ${results.length === 1 ? "livro encontrado" : "livros encontrados"}.`;

  return (
    <div className="reader-page">
      <div className="reader-page-head"><div><span className="section-index">SUA PRÓXIMA LEITURA</span><h1>Adicione um livro<br /><em>à sua estante.</em></h1><p>Busque pelo título, autor ou ISBN. Se ele não aparecer, cadastre os dados básicos.</p></div></div>
      <form className="catalog-search" onSubmit={search} role="search" aria-busy={searching}><label htmlFor="book-query">Buscar livros</label><div><span aria-hidden="true">⌕</span><input id="book-query" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Título, autor ou ISBN…" autoComplete="off" maxLength={100} /><button type="submit" disabled={!query.trim() || !!busy || searching}>{searching ? "Buscando…" : "Buscar"} <span aria-hidden="true">↗</span></button></div></form>
      <div className="sr-only" aria-live="polite" aria-atomic="true">{resultMessage}</div>
      {error && <div className="form-alert" role="alert">{error}</div>}
      {libraryWarning && <div className="form-alert" role="status">{libraryWarning}</div>}
      <div className="library-count"><span>{searched ? "RESULTADOS DA BUSCA" : "LIVROS DO CATÁLOGO"}</span><span className="library-rule" /></div>
      {!results && !error && <div className="search-loading" role="status">Procurando histórias…</div>}
      {results?.length === 0 && <div className="empty-state compact"><div className="empty-symbol" aria-hidden="true">⌕</div><h2>Nenhum livro encontrado.</h2><p>Revise a busca ou adicione o livro manualmente logo abaixo.</p></div>}
      {!!results?.length && <div className="catalog-list">{results.map((book) => {
        const key = `${book.source}:${book.id ?? `${book.externalProvider}:${book.externalId}`}`;
        const isAdded = !!book.id && libraryBookIds.has(book.id);
        const canAdd = !!book.id || (!!book.externalId && !!book.externalProvider);
        return <article className="catalog-item" key={key}><BookCover title={book.title} coverUrl={book.coverUrl} /><div className="catalog-info"><div className="catalog-labels"><span className="book-status">{book.source === "external" ? "CATÁLOGO EXTERNO" : "CATÁLOGO MNEMORA"}</span><span className={`pack-label${book.memoryPackAvailable ? " pack-label-ready" : ""}`}>{book.memoryPackAvailable ? "MEMÓRIA DISPONÍVEL" : "SOMENTE ESTANTE"}</span></div><h2>{book.title}</h2><p>{book.author}</p></div><button className="button button-outline" type="button" onClick={() => void add(book)} disabled={!!busy || !canAdd || isAdded}>{isAdded ? "Já adicionado" : busy === (book.id ?? `${book.externalProvider}:${book.externalId}`) ? "Adicionando…" : "Adicionar"} {!isAdded && <span aria-hidden="true">＋</span>}</button></article>;
      })}</div>}
      <section className={`manual-book${manualOpen ? " manual-book-open" : ""}`} aria-labelledby="manual-book-title">
        <div className="manual-book-intro"><div><span className="section-index">NÃO ENCONTROU?</span><h2 id="manual-book-title">Coloque seu livro na estante.</h2><p>Cadastre o título e, se souber, o autor e o ISBN. Você poderá acompanhar o status, a página e suas notas.</p></div><button type="button" className="button button-outline" aria-expanded={manualOpen} aria-controls="manual-book-form" disabled={!!busy} onClick={() => { setManualOpen((open) => !open); setManualError(null); }}>{manualOpen ? "Fechar formulário" : "Adicionar manualmente"} <span aria-hidden="true">{manualOpen ? "−" : "＋"}</span></button></div>
        {manualOpen && <form id="manual-book-form" className="manual-book-form" onSubmit={addManual}>
          {manualError && <div className="form-alert" role="alert">{manualError}</div>}
          <div className="field"><label htmlFor="manual-title">Título</label><input ref={manualTitleRef} id="manual-title" value={manualTitle} onChange={(event) => setManualTitle(event.target.value)} required maxLength={300} autoComplete="off" placeholder="Ex.: Dom Casmurro" /></div>
          <div className="field"><label htmlFor="manual-author">Autor <span>(opcional)</span></label><input id="manual-author" value={manualAuthor} onChange={(event) => setManualAuthor(event.target.value)} maxLength={300} autoComplete="off" placeholder="Ex.: Machado de Assis" /></div>
          <div className="field"><label htmlFor="manual-isbn">ISBN <span>(opcional)</span></label><input id="manual-isbn" value={manualIsbn} onChange={(event) => setManualIsbn(event.target.value)} maxLength={32} autoComplete="off" placeholder="ISBN-10 ou ISBN-13" aria-describedby="manual-isbn-help" /><span id="manual-isbn-help" className="field-help">Você encontra esse número na ficha catalográfica ou no código de barras.</span></div>
          <button className="button button-ink" type="submit" disabled={!!busy || !manualTitle.trim()}>{busy === "manual" ? "Adicionando…" : "Adicionar à minha estante"} <span aria-hidden="true">↗</span></button>
        </form>}
      </section>
    </div>
  );
}
