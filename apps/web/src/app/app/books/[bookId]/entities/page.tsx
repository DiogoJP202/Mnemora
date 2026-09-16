import type { Metadata } from "next";
import { LoreCollection } from "@/components/reader/lore-collection";

export const metadata: Metadata = { title: "Conhecimento" };

const config = {
  title: "O que você",
  emphasis: "já conhece.",
  intro: "Pessoas, lugares, grupos e acontecimentos reunidos até o seu ponto de leitura.",
  endpoint: "/entities",
  segment: "/entities",
  emptyNoun: "conhecimento",
};

export default async function EntitiesPage({ params }: { params: Promise<{ bookId: string }> }) {
  const { bookId } = await params;
  return <LoreCollection bookId={bookId} config={config} />;
}
