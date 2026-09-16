import type { Metadata } from "next";
import { LoreCollection } from "@/components/reader/lore-collection";

export const metadata: Metadata = { title: "Facções" };

const config = {
  title: "Grupos que já",
  emphasis: "entraram em cena.",
  intro: "Facções e organizações conhecidas, com apenas as pistas que sua leitura já revelou.",
  endpoint: "/factions",
  segment: "/factions",
  emptyNoun: "grupo",
};

export default async function FactionsPage({ params }: { params: Promise<{ bookId: string }> }) {
  const { bookId } = await params;
  return <LoreCollection bookId={bookId} config={config} />;
}
