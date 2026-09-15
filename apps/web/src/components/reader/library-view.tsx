"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { apiGet } from "@/lib/api";
import { BookCover } from "@/components/reader/book-cover";
import type { LibraryBook } from "@/lib/reader-types";

const statusLabel: Record<LibraryBook["status"], string> = {
  WantToRead: "Quero ler",
  Reading: "Lendo",
  Paused: "Pausado",
  Finished: "Concluído",
};

export function LibraryView() {
  const [books, setBooks] = useState<LibraryBook[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    apiGet<LibraryBook[]>("/api/library").then(setBooks).catch((cause) => setError(cause instanceof Error ? cause.message : "Não foi possível carregar sua biblioteca."));
  }, []);

  return (
    <div className="reader-page">
      <div className="reader-page-head"><div><span className="section-index">SEU LUGAR NA HISTÓRIA</span><h1>Minha <em>biblioteca.</em></h1><p>As histórias que estão com você agora.</p></div><Link href="/app/search" className="button button-ink">Adicionar livro <span aria-hidden="true">＋</span></Link></div>
      {error && <div className="form-alert" role="alert">{error}<button type="button" onClick={() => location.reload()}>Tentar novamente</button></div>}
      {!books && !error && <div className="card-grid" aria-label="Carregando livros"><div className="book-card skeleton" /><div className="book-card skeleton" /></div>}
      {books?.length === 0 && <div className="empty-state"><div className="empty-symbol" aria-hidden="true">◫</div><span className="section-index">O COMEÇO DE UMA HISTÓRIA</span><h2>Sua estante espera<br /><em>o primeiro livro.</em></h2><p>Adicione uma leitura para acompanhar seu progresso e recuperar o que você já conhece.</p><Link className="button button-ink" href="/app/search">Encontrar um livro <span aria-hidden="true">↗</span></Link></div>}
      {!!books?.length && <><div className="library-count"><span>{books.length === 1 ? "1 LIVRO NA SUA ESTANTE" : `${books.length} LIVROS NA SUA ESTANTE`}</span><span className="library-rule" /></div><div className="card-grid">{books.map((item) => <Link href={`/app/books/${item.book.id}`} className="book-card" key={item.book.id}><BookCover title={item.book.title} coverUrl={item.book.coverUrl} /><div className="book-card-info"><span className="book-status">{statusLabel[item.status] ?? item.status}</span><h2>{item.book.title}</h2><p>{item.book.author}</p><div className="book-progress"><div className="book-progress-track"><i style={{ width: `${Math.max(0, Math.min(100, item.progressPercentage ?? 0))}%` }} /></div><span>{item.currentReadingUnitId ? `${Math.round(item.progressPercentage ?? 0)}% da leitura` : "Ainda não começou"}</span></div></div><span className="book-card-arrow" aria-hidden="true">↗</span></Link>)}</div></>}
    </div>
  );
}
