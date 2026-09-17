"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { apiGet, apiWrite } from "@/lib/api";
import { BookLoreNav } from "@/components/reader/book-lore-nav";
import type { LoreEntitySummary } from "@/lib/lore-types";
import type { Book } from "@/lib/reader-types";

const typeLabel: Record<string, string> = {
  Character: "PERSONAGEM", Faction: "FACÇÃO", Location: "LUGAR",
  Creature: "CRIATURA", Item: "OBJETO", Event: "EVENTO",
  Organization: "ORGANIZAÇÃO", Concept: "CONCEITO", Other: "CONHECIMENTO",
};

export function ReviewView({ bookId }: { bookId: string }) {
  const [book, setBook] = useState<Book | null>(null);
  const [cards, setCards] = useState<LoreEntitySummary[] | null>(null);
  const [index, setIndex] = useState(0);
  const [remembered, setRemembered] = useState(0);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [announcement, setAnnouncement] = useState("");

  const load = useCallback(async () => {
    setError(null);
    const [foundBook, reviewCards] = await Promise.all([
      apiGet<Book>(`/api/books/${bookId}`),
      apiGet<LoreEntitySummary[]>(`/api/books/${bookId}/review`),
    ]);
    setBook(foundBook);
    setCards(reviewCards);
    setIndex(0);
    setRemembered(0);
  }, [bookId]);

  useEffect(() => {
    let active = true;
    Promise.all([
      apiGet<Book>(`/api/books/${bookId}`),
      apiGet<LoreEntitySummary[]>(`/api/books/${bookId}/review`),
    ]).then(([foundBook, reviewCards]) => {
      if (!active) return;
      setBook(foundBook);
      setCards(reviewCards);
    }).catch((cause) => {
      if (active) setError(cause instanceof Error ? cause.message : "Não foi possível preparar a revisão.");
    });
    return () => { active = false; };
  }, [bookId]);

  async function answer(value: boolean) {
    const card = cards?.[index];
    if (!card || saving) return;
    setSaving(true);
    setError(null);
    try {
      await apiWrite(`/api/books/${bookId}/review/${card.id}`, "POST", { remembered: value });
      if (value) setRemembered((count) => count + 1);
      setAnnouncement(value ? `${card.name}: você lembrou.` : `${card.name}: marcado para revisar.`);
      setIndex((position) => position + 1);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível registrar sua resposta.");
    } finally {
      setSaving(false);
    }
  }

  async function retryLoad() {
    try {
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível preparar a revisão.");
    }
  }

  const current = cards?.[index];
  const finished = !!cards?.length && index >= cards.length;

  return (
    <div className="reader-page lore-page review-page">
      <Link href={`/app/books/${bookId}`} className="back-link">← Voltar ao livro</Link>
      <div className="lore-page-head">
        <span className="section-index">{book?.title ?? "REVISÃO DO LIVRO"}</span>
        <h1>Reencontre cinco <em>lembranças.</em></h1>
        <p>Uma pausa curta para reforçar quem e o que você já conheceu na história.</p>
      </div>
      <BookLoreNav bookId={bookId} active="/review" />
      <div className="sr-only" aria-live="polite">{announcement}</div>
      {error && <div className="form-alert" role="alert">{error}<button type="button" onClick={() => void retryLoad()}>Tentar novamente</button></div>}
      {cards === null && !error && <div className="review-card skeleton" role="status"><span className="sr-only">Preparando revisão…</span></div>}
      {cards?.length === 0 && (
        <div className="empty-state compact review-empty">
          <div className="empty-symbol" aria-hidden="true">✦</div>
          <h2>Sua revisão começa com a leitura.</h2>
          <p>Marque onde você parou para revisar apenas as lembranças já reveladas.</p>
          <Link className="button button-outline" href={`/app/books/${bookId}`}>Marcar meu progresso <span aria-hidden="true">↗</span></Link>
        </div>
      )}
      {current && (
        <section className="review-session" aria-labelledby="review-entity-name">
          <div className="review-progress" role="progressbar" aria-label="Progresso da revisão" aria-valuemin={1} aria-valuemax={cards.length} aria-valuenow={index + 1}><span>{index + 1} DE {cards.length}</span><div aria-hidden="true">{cards.map((card, cardIndex) => <i key={card.id} className={cardIndex <= index ? "active" : ""} />)}</div></div>
          <article className="review-card">
            <div className="review-monogram" aria-hidden="true">{current.name.trim().charAt(0).toUpperCase()}</div>
            <span className="section-index">{typeLabel[current.type] ?? current.type}</span>
            <h2 id="review-entity-name">{current.name}</h2>
            <p>{current.memoryHint ?? current.shortDescription ?? "Uma lembrança que já faz parte da sua leitura."}</p>
            <Link href={`/app/books/${bookId}/entities/${current.id}`}>Ver o que já sei <span aria-hidden="true">↗</span></Link>
          </article>
          <div className="review-actions" aria-label="Como foi esta lembrança?">
            <button type="button" className="button button-outline" onClick={() => void answer(false)} disabled={saving}>Preciso revisar</button>
            <button type="button" className="button button-ink" onClick={() => void answer(true)} disabled={saving}>Eu lembro <span aria-hidden="true">✓</span></button>
          </div>
        </section>
      )}
      {finished && (
        <div className="review-complete">
          <span aria-hidden="true">✦</span>
          <span className="section-index">REVISÃO CONCLUÍDA</span>
          <h2>{remembered} de {cards.length} lembranças estavam frescas.</h2>
          <p>Seu registro ficou salvo para ajudar uma versão futura do Mnemora a escolher revisões melhores.</p>
          <div><button type="button" className="button button-outline" onClick={() => void retryLoad()}>Revisar novamente</button><Link className="button button-ink" href={`/app/books/${bookId}`}>Voltar ao livro <span aria-hidden="true">↗</span></Link></div>
        </div>
      )}
    </div>
  );
}
