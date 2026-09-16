import type { Metadata } from "next";
import { TimelineView } from "@/components/reader/timeline-view";

export const metadata: Metadata = { title: "Timeline" };

export default async function TimelinePage({ params }: { params: Promise<{ bookId: string }> }) {
  const { bookId } = await params;
  return <TimelineView bookId={bookId} />;
}
