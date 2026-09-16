import type { Metadata } from "next";
import { LoreEntityDetailView } from "@/components/reader/lore-entity-detail";

export const metadata: Metadata = { title: "Lembrança" };

export default async function EntityPage({ params }: { params: Promise<{ bookId: string; entityId: string }> }) {
  const { bookId, entityId } = await params;
  return <LoreEntityDetailView bookId={bookId} entityId={entityId} />;
}
