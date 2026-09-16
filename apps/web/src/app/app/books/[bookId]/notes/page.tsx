import type { Metadata } from "next";
import { BookNotesView } from "@/components/reader/book-notes-view";

export const metadata: Metadata = { title: "Notas privadas" };

export default async function NotesPage({ params }: { params: Promise<{ bookId: string }> }) {
  const { bookId } = await params;
  return <BookNotesView bookId={bookId} />;
}
