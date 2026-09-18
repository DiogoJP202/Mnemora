"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { apiGet } from "@/lib/api";
import { BookLoreNav } from "@/components/reader/book-lore-nav";
import { LoreEntityCard } from "@/components/reader/lore-entity-card";
import { MemoryPackUnavailable } from "@/components/reader/memory-pack-unavailable";
import type { Book, LibraryBook } from "@/lib/reader-types";
import type { LoreEntitySummary } from "@/lib/lore-types";

type CollectionConfig = {
  title: string;
  emphasis: string;
  intro: string;
  endpoint: string;
  segment: string;
  emptyNoun: string;
};

export function LoreCollection({ bookId, config }: { bookId: string; config: CollectionConfig }) {
  const [book, setBook] = useState<Book | null>(null);
  const [entities, setEntities] = useState<LoreEntitySummary[] | null>(null);
  const [hasProgress, setHasProgress] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [retry, setRetry] = useState(0);

  useEffect(() => {
    let active = true;
    Promise.all([
      apiGet<Book>(`/api/books/${bookId}`),
      apiGet<LibraryBook[]>("/api/library"),
    ]).then(async ([foundBook, library]) => {
      if (!active) return;
      setBook(foundBook);
      setHasProgress(!!library.find((item) => item.book.id === bookId)?.currentReadingUnitId);
      if (!foundBook.memoryPackAvailable) {
        setEntities([]);
        return;
      }
      const knownEntities = await apiGet<LoreEntitySummary[]>(
        `/api/books/${bookId}${config.endpoint}`,
      );
      if (active) setEntities(knownEntities);
    }).catch((cause) => {
      if (active) setError(cause instanceof Error ? cause.message : "Não foi possível abrir este conhecimento.");
    });
    return () => { active = false; };
  }, [bookId, config.endpoint, retry]);

  return <div className="reader-page lore-page">
    <Link href={`/app/books/${bookId}`} className="back-link">← Voltar ao livro</Link>
    <div className="lore-page-head"><span className="section-index">{book?.title ?? "EXPLORAR O LIVRO"}</span><h1>{config.title} <em>{config.emphasis}</em></h1><p>{config.intro}</p></div>
    <BookLoreNav bookId={bookId} active={config.segment} memoryPackAvailable={book?.memoryPackAvailable ?? false} />
    {book && !book.memoryPackAvailable ? <MemoryPackUnavailable bookId={bookId} /> : <>
      {error && <div className="form-alert" role="alert">{error}<button type="button" onClick={() => { setEntities(null); setError(null); setRetry((value) => value + 1); }}>Tentar novamente</button></div>}
      {!entities && !error && <div className="lore-grid" role="status"><span className="sr-only">Carregando conhecimento…</span><div className="lore-card skeleton" /><div className="lore-card skeleton" /></div>}
      {entities?.length === 0 && <div className="empty-state compact lore-empty"><div className="empty-symbol" aria-hidden="true">◇</div><h2>{hasProgress ? `Nenhum ${config.emptyNoun} encontrado.` : "Comece pela sua leitura."}</h2><p>{hasProgress ? "Não encontrei lembranças deste tipo dentro do que você já leu." : "Marque a unidade onde você parou para explorar somente o que já conhece."}</p><Link href={`/app/books/${bookId}`} className="button button-outline">{hasProgress ? "Voltar ao livro" : "Marcar meu progresso"} <span aria-hidden="true">↗</span></Link></div>}
      {!!entities?.length && <div className="lore-grid">{entities.map((entity) => <LoreEntityCard key={entity.id} entity={entity} />)}</div>}
    </>}
  </div>;
}
