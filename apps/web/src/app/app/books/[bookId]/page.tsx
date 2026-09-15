import type { Metadata } from "next";
import { BookDetail } from "@/components/reader/book-detail";
export const metadata: Metadata = { title: "Livro" };
export default async function BookPage({ params }: { params: Promise<{ bookId: string }> }) {
  const { bookId } = await params;
  return <BookDetail bookId={bookId} />;
}
