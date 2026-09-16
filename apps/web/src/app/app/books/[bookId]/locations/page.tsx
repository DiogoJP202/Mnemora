import type { Metadata } from "next";
import { LoreCollection } from "@/components/reader/lore-collection";

export const metadata: Metadata = { title: "Lugares" };

const config = {
  title: "Lugares pelos quais",
  emphasis: "você já passou.",
  intro: "Um mapa de nomes e pistas limitado aos cenários apresentados até agora.",
  endpoint: "/locations",
  segment: "/locations",
  emptyNoun: "lugar",
};

export default async function LocationsPage({ params }: { params: Promise<{ bookId: string }> }) {
  const { bookId } = await params;
  return <LoreCollection bookId={bookId} config={config} />;
}
