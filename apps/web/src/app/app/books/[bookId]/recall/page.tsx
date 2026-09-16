import type { Metadata } from "next";
import { RecallView } from "@/components/reader/recall-view";

export const metadata: Metadata = { title: "Recall" };

export default async function RecallPage({ params }: { params: Promise<{ bookId: string }> }) {
  const { bookId } = await params;
  return <RecallView bookId={bookId} />;
}
