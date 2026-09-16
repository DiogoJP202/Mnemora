import type { Metadata } from "next";
import { ReviewView } from "@/components/reader/review-view";

export const metadata: Metadata = { title: "Revisão" };

export default async function ReviewPage({ params }: { params: Promise<{ bookId: string }> }) {
  const { bookId } = await params;
  return <ReviewView bookId={bookId} />;
}
