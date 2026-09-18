"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { apiGet } from "@/lib/api";
import { BookLoreNav } from "@/components/reader/book-lore-nav";
import { MemoryPackUnavailable } from "@/components/reader/memory-pack-unavailable";
import type { LoreEntitySummary } from "@/lib/lore-types";
import type { Book } from "@/lib/reader-types";

const typeLabel: Record<string, string> = {
  Character: "PERSONAGEM",
  Faction: "FACÇÃO",
  Location: "LUGAR",
  Creature: "CRIATURA",
  Item: "OBJETO",
  Event: "EVENTO",
  Organization: "ORGANIZAÇÃO",
  Concept: "CONCEITO",
  Other: "CONHECIMENTO",
};

export function RecallView({ bookId }: { bookId: string }) {
  const [book, setBook] = useState<Book | null>(null);
  const [query, setQuery] = useState("");
  const [searchedQuery, setSearchedQuery] = useState("");
  const [results, setResults] = useState<LoreEntitySummary[] | null>(null);
  const [searching, setSearching] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    apiGet<Book>(`/api/books/${bookId}`)
      .then((foundBook) => {
        if (active) setBook(foundBook);
      })
      .catch((cause) => {
        if (active) {
          setError(cause instanceof Error ? cause.message : "Não foi possível abrir o Recall.");
        }
      });
    return () => { active = false; };
  }, [bookId]);

  async function runSearch(term: string) {
    setSearching(true);
    setError(null);
    setResults(null);
    setSearchedQuery(term);
    try {
      setResults(await apiGet<LoreEntitySummary[]>(
        `/api/books/${bookId}/recall?q=${encodeURIComponent(term)}`,
      ));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível buscar suas lembranças.");
    } finally {
      setSearching(false);
    }
  }

  function search(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const term = query.trim();
    if (term && !searching) void runSearch(term);
  }

  const liveMessage = searching
    ? "Buscando nas lembranças disponíveis."
    : error
      ? error
      : results
        ? results.length === 0
          ? "Nenhuma lembrança encontrada."
          : `${results.length} ${results.length === 1 ? "lembrança encontrada" : "lembranças encontradas"}.`
        : "";

  return (
    <div className="reader-page lore-page recall-page">
      <Link href={`/app/books/${bookId}`} className="back-link">← Voltar ao livro</Link>
      <div className="lore-page-head">
        <span className="section-index">{book?.title ?? "RECALL DO LIVRO"}</span>
        <h1>Encontre o que a memória <em>quase guardou.</em></h1>
        <p>Busque por um nome, apelido ou detalhe que você já encontrou na história.</p>
      </div>
      <BookLoreNav bookId={bookId} active="/recall" memoryPackAvailable={book?.memoryPackAvailable ?? false} />
      {!book && !error && <div className="search-loading" role="status">Preparando o Recall…</div>}
      {error && (
        <div className="form-alert" role="alert">
          {error}
          {searchedQuery && (
            <button type="button" onClick={() => void runSearch(searchedQuery)}>Tentar novamente</button>
          )}
        </div>
      )}
      {book && !book.memoryPackAvailable ? <MemoryPackUnavailable bookId={bookId} /> : book ? <>
      <form className="recall-search" role="search" onSubmit={search}>
        <label htmlFor="recall-query">O que você quer relembrar?</label>
        <div>
          <span aria-hidden="true">⌕</span>
          <input
            id="recall-query"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Nome, lugar ou detalhe lembrado…"
            autoComplete="off"
            maxLength={100}
            aria-describedby="recall-help"
          />
          <button
            type="submit"
            disabled={!query.trim() || searching}
            aria-controls="recall-results"
          >
            {searching ? "Buscando…" : "Buscar"} <span aria-hidden="true">↗</span>
          </button>
        </div>
        <p id="recall-help">O Mnemora consulta somente o conhecimento disponível no seu ponto de leitura.</p>
      </form>

      <div className="sr-only" aria-live="polite" aria-atomic="true">{liveMessage}</div>
      <section id="recall-results" className="recall-results" aria-busy={searching} aria-label="Resultados do Recall">
        {!searchedQuery && !error && (
          <div className="recall-prompt">
            <span aria-hidden="true">✦</span>
            <div><strong>Uma pista já basta.</strong><p>Digite o fragmento que ficou na memória e faça uma busca.</p></div>
          </div>
        )}

        {searching && (
          <div className="recall-result-list" aria-hidden="true">
            <div className="recall-result skeleton" />
            <div className="recall-result skeleton" />
          </div>
        )}

        {!searching && results?.length === 0 && (
          <div className="empty-state compact recall-empty">
            <div className="empty-symbol" aria-hidden="true">⌕</div>
            <h2>Nenhuma lembrança encontrada.</h2>
            <p>Não encontrei correspondências para “{searchedQuery}” entre as lembranças disponíveis.</p>
          </div>
        )}

        {!searching && !!results?.length && (
          <>
            <div className="library-count">
              <span>{results.length} {results.length === 1 ? "LEMBRANÇA" : "LEMBRANÇAS"}</span>
              <span className="library-rule" />
            </div>
            <ul className="recall-result-list">
              {results.map((entity) => (
                <li key={entity.id}>
                  <Link href={`/app/books/${entity.bookId}/entities/${entity.id}`} className="recall-result">
                    <span className="recall-result-symbol" aria-hidden="true">{entity.name.trim().charAt(0).toUpperCase()}</span>
                    <span className="recall-result-copy">
                      <span className="book-status">{typeLabel[entity.type] ?? entity.type}</span>
                      <strong>{entity.name}</strong>
                      {(entity.memoryHint || entity.shortDescription) && <span>{entity.memoryHint ?? entity.shortDescription}</span>}
                    </span>
                    <span className="recall-result-arrow" aria-hidden="true">↗</span>
                  </Link>
                </li>
              ))}
            </ul>
          </>
        )}
      </section>
      </> : null}
    </div>
  );
}
