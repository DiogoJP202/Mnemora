import Link from "next/link";
import type { LoreEntitySummary } from "@/lib/lore-types";

const typeLabel: Record<string, string> = {
  Character: "PERSONAGEM", Faction: "FACÇÃO", Location: "LUGAR",
  Creature: "CRIATURA", Item: "OBJETO", Event: "EVENTO",
  Organization: "ORGANIZAÇÃO", Concept: "CONCEITO", Other: "CONHECIMENTO",
};

export function LoreEntityCard({ entity }: { entity: LoreEntitySummary }) {
  return <Link href={`/app/books/${entity.bookId}/entities/${entity.id}`} className="lore-card"><span className="lore-card-symbol" aria-hidden="true">{entity.name.trim().charAt(0).toUpperCase()}</span><div className="lore-card-copy"><span className="book-status">{typeLabel[entity.type] ?? entity.type}</span><h2>{entity.name}</h2>{entity.memoryHint && <p className="lore-hint">{entity.memoryHint}</p>}{!entity.memoryHint && entity.shortDescription && <p>{entity.shortDescription}</p>}</div><span className="lore-card-arrow" aria-hidden="true">↗</span></Link>;
}
