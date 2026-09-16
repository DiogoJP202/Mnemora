import type { Metadata } from "next";
import { LoreCollection } from "@/components/reader/lore-collection";

export const metadata: Metadata = { title: "Personagens" };

const config = {
  title: "Rostos que você",
  emphasis: "já encontrou.",
  intro: "Relembre quem apareceu na história sem descobrir quem ainda está por vir.",
  endpoint: "/entities?type=Character",
  segment: "/characters",
  emptyNoun: "personagem",
};

export default async function CharactersPage({ params }: { params: Promise<{ bookId: string }> }) {
  const { bookId } = await params;
  return <LoreCollection bookId={bookId} config={config} />;
}
