"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { BookLoreNav } from "@/components/reader/book-lore-nav";
import { apiGet } from "@/lib/api";
import type { TimelineEvent } from "@/lib/lore-types";
import type { Book, LibraryBook } from "@/lib/reader-types";

export function TimelineView({ bookId }: { bookId: string }) {
  const [book, setBook] = useState<Book | null>(null);
  const [events, setEvents] = useState<TimelineEvent[] | null>(null);
  const [hasProgress, setHasProgress] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [retry, setRetry] = useState(0);

  useEffect(() => {
    let active = true;
    Promise.all([
      apiGet<Book>(`/api/books/${bookId}`),
      apiGet<LibraryBook[]>("/api/library"),
      apiGet<TimelineEvent[]>(`/api/books/${bookId}/timeline`),
    ]).then(([foundBook, library, knownEvents]) => {
      if (!active) return;
      setBook(foundBook);
      setEvents(knownEvents);
      setHasProgress(!!library.find((item) => item.book.id === bookId)?.currentReadingUnitId);
    }).catch((cause) => {
      if (active) setError(cause instanceof Error ? cause.message : "Não foi possível abrir a timeline.");
    });
    return () => { active = false; };
  }, [bookId, retry]);

  function tryAgain() {
    setEvents(null);
    setError(null);
    setRetry((value) => value + 1);
  }

  return <div className="reader-page lore-page timeline-page">
    <Link href={`/app/books/${bookId}`} className="back-link">← Voltar ao livro</Link>
    <div className="lore-page-head"><span className="section-index">{book?.title ?? "EXPLORAR O LIVRO"}</span><h1>Sua história, <em>até aqui.</em></h1><p>Os acontecimentos aparecem na ordem da narrativa e param exatamente no seu ponto de leitura.</p></div>
    <BookLoreNav bookId={bookId} active="/timeline" />
    {error && <div className="form-alert" role="alert">{error}<button type="button" onClick={tryAgain}>Tentar novamente</button></div>}
    {!events && !error && <div className="timeline-list" role="status"><span className="sr-only">Carregando timeline…</span><div className="timeline-item skeleton" /><div className="timeline-item skeleton" /></div>}
    {events?.length === 0 && <div className="empty-state compact lore-empty"><div className="empty-symbol" aria-hidden="true">⌁</div><h2>{hasProgress ? "Nenhum evento conhecido." : "A timeline começa com a leitura."}</h2><p>{hasProgress ? "Ainda não há acontecimentos registrados dentro do trecho que você leu." : "Marque onde você parou para ver apenas os acontecimentos já revelados."}</p><Link href={`/app/books/${bookId}`} className="button button-outline">{hasProgress ? "Voltar ao livro" : "Marcar meu progresso"} <span aria-hidden="true">↗</span></Link></div>}
    {!!events?.length && <ol className="timeline-list">{events.map((event, index) => <li className="timeline-item" key={event.entity.id}><span className="timeline-marker" aria-hidden="true">{String(index + 1).padStart(2, "0")}</span><div className="timeline-copy"><span className="book-status">ACONTECIMENTO REVELADO</span><h2><Link href={`/app/books/${bookId}/entities/${event.entity.id}`}>{event.entity.name}</Link></h2>{event.entity.memoryHint && <p className="lore-hint">{event.entity.memoryHint}</p>}{!event.entity.memoryHint && event.entity.shortDescription && <p>{event.entity.shortDescription}</p>}</div><Link className="timeline-link" href={`/app/books/${bookId}/entities/${event.entity.id}`} aria-label={`Abrir ${event.entity.name}`}>↗</Link></li>)}</ol>}
  </div>;
}
