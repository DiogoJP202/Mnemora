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
    <div className="reader-page" aria-busy={!books && !error}>
      <div className="reader-page-head"><div><span className="section-index">SEU LUGAR NA HISTÓRIA</span><h1>Minha <em>biblioteca.</em></h1><p>As histórias que estão com você agora.</p></div><Link href="/app/search" className="button button-ink">Adicionar livro <span aria-hidden="true">＋</span></Link></div>
      {error && <div className="form-alert" role="alert">{error}<button type="button" onClick={() => location.reload()}>Tentar novamente</button></div>}
      {!books && !error && <div className="card-grid" role="status"><span className="sr-only">Carregando sua biblioteca…</span><div className="book-card skeleton" aria-hidden="true" /><div className="book-card skeleton" aria-hidden="true" /></div>}
      {books?.length === 0 && <div className="empty-state"><div className="empty-symbol" aria-hidden="true">◫</div><span className="section-index">O COMEÇO DE UMA HISTÓRIA</span><h2>Sua estante espera<br /><em>o primeiro livro.</em></h2><p>Adicione uma leitura para acompanhar seu progresso e recuperar o que você já conhece.</p><Link className="button button-ink" href="/app/search">Encontrar um livro <span aria-hidden="true">↗</span></Link></div>}
      {!!books?.length && <><div className="library-count"><span>{books.length === 1 ? "1 LIVRO NA SUA ESTANTE" : `${books.length} LIVROS NA SUA ESTANTE`}</span><span className="library-rule" /></div><div className="card-grid">{books.map((item) => {
        const progress = Math.max(0, Math.min(100, item.progressPercentage ?? 0));
        const packAvailable = item.book.memoryPackAvailable;
        return <Link href={`/app/books/${item.book.id}`} className="book-card" key={item.book.id}><BookCover title={item.book.title} coverUrl={item.book.coverUrl} /><div className="book-card-info"><span className="book-status">{statusLabel[item.status] ?? item.status}</span><h2>{item.book.title}</h2><p>{item.book.author}</p>{packAvailable ? <div className="book-progress"><div className="book-progress-track" role="progressbar" aria-label={`Progresso em ${item.book.title}`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(progress)}><i aria-hidden="true" style={{ width: `${progress}%` }} /></div><span>{item.currentReadingUnitId ? `${Math.round(progress)}% da leitura` : "Ainda não começou"}</span></div> : <div className="book-progress shelf-card-progress"><span>{item.currentPage ? `Página ${item.currentPage}` : "Página ainda não informada"}</span><small>Somente estante</small></div>}</div><span className="book-card-arrow" aria-hidden="true">↗</span></Link>;
      })}</div></>}
    </div>
  );
}
