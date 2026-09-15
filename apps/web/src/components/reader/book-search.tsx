"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiGet, apiWrite } from "@/lib/api";
import { BookCover } from "@/components/reader/book-cover";
import type { Book, LibraryBook, SearchBook } from "@/lib/reader-types";

export function BookSearch() {
  const router = useRouter();
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SearchBook[] | null>(null);
  const [searched, setSearched] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    apiGet<Book[]>("/api/books").then((books) => setResults(books.map((book) => ({ id: book.id, externalId: null, source: "local" as const, title: book.title, author: book.author, coverUrl: book.coverUrl })))).catch((cause) => setError(cause instanceof Error ? cause.message : "Não foi possível carregar o catálogo."));
  }, []);

  async function search(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const term = query.trim();
    if (!term) return;
    setSearched(true);
    setResults(null);
    setError(null);
    try {
      setResults(await apiGet<SearchBook[]>(`/api/books/search?q=${encodeURIComponent(term)}`));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível fazer a busca.");
    }
  }

  async function add(book: SearchBook) {
    const key = book.id ?? book.externalId;
    if (!key) return;
    setBusy(key);
    setError(null);
    try {
      const libraryBook = book.source === "external"
        ? await apiWrite<LibraryBook>("/api/library/external", "POST", { externalId: book.externalId })
        : await apiWrite<LibraryBook>(`/api/library/${book.id}`, "POST");
      router.push(`/app/books/${libraryBook.book.id}`);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível adicionar o livro.");
      setBusy(null);
    }
  }

  return (
    <div className="reader-page">
      <div className="reader-page-head"><div><span className="section-index">ENCONTRE SUA PRÓXIMA LEITURA</span><h1>Um livro para<br /><em>sua estante.</em></h1><p>Busque pelo título ou autor. Depois, marque até onde você leu.</p></div></div>
      <form className="catalog-search" onSubmit={search} role="search"><label htmlFor="book-query">Buscar livros</label><div><span aria-hidden="true">⌕</span><input id="book-query" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Título, autor ou ISBN…" /><button type="submit" disabled={!query.trim()}>Buscar <span aria-hidden="true">↗</span></button></div></form>
      {error && <div className="form-alert" role="alert">{error}</div>}
      <div className="library-count"><span>{searched ? "RESULTADOS DA BUSCA" : "LIVROS DO CATÁLOGO"}</span><span className="library-rule" /></div>
      {!results && !error && <div className="search-loading" role="status">Procurando histórias…</div>}
      {results?.length === 0 && <div className="empty-state compact"><div className="empty-symbol" aria-hidden="true">⌕</div><h2>Nenhum livro encontrado.</h2><p>Tente outro título ou autor. O catálogo pode crescer com suas buscas.</p></div>}
      {!!results?.length && <div className="catalog-list">{results.map((book) => {
        const key = `${book.source}:${book.id ?? book.externalId}`;
        return <article className="catalog-item" key={key}><BookCover title={book.title} coverUrl={book.coverUrl} /><div className="catalog-info"><span className="book-status">{book.source === "external" ? "CATÁLOGO EXTERNO" : "CATÁLOGO MNEMORA"}</span><h2>{book.title}</h2><p>{book.author}</p></div><button className="button button-outline" type="button" onClick={() => add(book)} disabled={!!busy || !(book.id ?? book.externalId)}>{busy === (book.id ?? book.externalId) ? "Adicionando…" : "Adicionar"} <span aria-hidden="true">＋</span></button></article>;
      })}</div>}
    </div>
  );
}
