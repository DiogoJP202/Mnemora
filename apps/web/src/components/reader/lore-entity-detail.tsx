"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { BookLoreNav } from "@/components/reader/book-lore-nav";
import { apiGet } from "@/lib/api";
import type { LoreEntityDetail, LoreRelation } from "@/lib/lore-types";

const typeLabel: Record<string, string> = {
  Character: "PERSONAGEM", Faction: "FACÇÃO", Location: "LUGAR",
  Creature: "CRIATURA", Item: "OBJETO", Event: "EVENTO",
  Organization: "ORGANIZAÇÃO", Concept: "CONCEITO", Other: "CONHECIMENTO",
};

const factLabel: Record<string, string> = {
  Description: "DESCRIÇÃO", Title: "TÍTULO", Background: "HISTÓRIA",
  Event: "ACONTECIMENTO", RelationshipContext: "RELAÇÃO", Other: "LEMBRANÇA",
};

function RelatedEntity({ relation, entityId, bookId }: { relation: LoreRelation; entityId: string; bookId: string }) {
  const relatedId = relation.sourceId === entityId ? relation.targetId : relation.sourceId;
  const relatedName = relation.sourceId === entityId ? relation.targetName : relation.sourceName;
  return <Link className="relation-card" href={`/app/books/${bookId}/entities/${relatedId}`}>
    <span className="relation-kicker">{relation.label ?? relation.relationType}</span>
    <strong>{relatedName}</strong>
    <span aria-hidden="true">↗</span>
  </Link>;
}

export function LoreEntityDetailView({ bookId, entityId }: { bookId: string; entityId: string }) {
  const [detail, setDetail] = useState<LoreEntityDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [retry, setRetry] = useState(0);

  useEffect(() => {
    let active = true;
    apiGet<LoreEntityDetail>(`/api/entities/${entityId}`)
      .then((found) => {
        if (!active) return;
        if (found.entity.bookId !== bookId) throw new Error("Este conhecimento não pertence ao livro informado.");
        setDetail(found);
      })
      .catch((cause) => {
        if (active) setError(cause instanceof Error ? cause.message : "Não foi possível abrir esta lembrança.");
      })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [bookId, entityId, retry]);

  function tryAgain() {
    setDetail(null);
    setError(null);
    setLoading(true);
    setRetry((value) => value + 1);
  }

  if (loading) return <div className="reader-page entity-detail-page" role="status"><span className="sr-only">Carregando lembrança…</span><div className="entity-detail-hero skeleton" /></div>;
  if (!detail) return <div className="reader-page"><Link href={`/app/books/${bookId}/entities`} className="back-link">← Voltar ao conhecimento</Link><div className="form-alert" role="alert"><span>{error ?? "Lembrança não encontrada no seu ponto de leitura."}</span><button type="button" onClick={tryAgain}>Tentar novamente</button></div></div>;

  const { entity, aliases, facts, relations } = detail;
  return <div className="reader-page entity-detail-page">
    <Link href={`/app/books/${bookId}/entities`} className="back-link">← Voltar ao conhecimento</Link>
    <BookLoreNav bookId={bookId} active="/entities" />
    <header className="entity-detail-hero">
      <div className="entity-monogram" aria-hidden="true">{entity.name.trim().charAt(0).toUpperCase()}</div>
      <div className="entity-detail-copy"><span className="section-index">{typeLabel[entity.type] ?? entity.type}</span><h1>{entity.name}</h1>{entity.shortDescription && <p>{entity.shortDescription}</p>}{aliases.length > 0 && <div className="alias-list" aria-label="Também conhecido como">{aliases.map((alias) => <span key={alias}>{alias}</span>)}</div>}</div>
    </header>
    {entity.memoryHint && <aside className="memory-callout"><span aria-hidden="true">✦</span><div><span className="section-index">PISTA DE MEMÓRIA</span><p>{entity.memoryHint}</p></div></aside>}
    <div className="entity-knowledge-layout">
      <section aria-labelledby="facts-title"><span className="section-index">O QUE VOCÊ JÁ SABE</span><h2 id="facts-title">Lembranças reveladas</h2>{facts.length > 0 ? <div className="fact-list">{facts.map((fact) => <article className="fact-card" key={fact.id}><span>{factLabel[fact.type] ?? fact.type}</span><p>{fact.content}</p>{fact.memoryHint && <small>{fact.memoryHint}</small>}</article>)}</div> : <div className="knowledge-empty"><p>Ainda não há fatos registrados neste ponto da leitura.</p></div>}</section>
      <aside aria-labelledby="relations-title"><span className="section-index">CONEXÕES CONHECIDAS</span><h2 id="relations-title">Relações</h2>{relations.length > 0 ? <div className="relation-list">{relations.map((relation) => <RelatedEntity key={relation.id} relation={relation} entityId={entity.id} bookId={bookId} />)}</div> : <div className="knowledge-empty"><p>Nenhuma relação foi revelada até aqui.</p></div>}</aside>
    </div>
  </div>;
}
