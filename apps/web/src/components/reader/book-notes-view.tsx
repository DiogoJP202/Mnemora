"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { apiGet } from "@/lib/api";
import { BookLoreNav } from "@/components/reader/book-lore-nav";
import { NotesPanel } from "@/components/reader/notes-panel";
import type { Book } from "@/lib/reader-types";

export function BookNotesView({ bookId }: { bookId: string }) {
  const [book, setBook] = useState<Book | null>(null);

  useEffect(() => {
    let active = true;
    apiGet<Book>(`/api/books/${bookId}`).then((value) => {
      if (active) setBook(value);
    }).catch(() => undefined);
    return () => { active = false; };
  }, [bookId]);

  return (
    <div className="reader-page lore-page notes-page">
      <Link href={`/app/books/${bookId}`} className="back-link">← Voltar ao livro</Link>
      <div className="lore-page-head">
        <span className="section-index">{book?.title ?? "NOTAS DO LIVRO"}</span>
        <h1>Um lugar para o que <em>você percebeu.</em></h1>
        <p>Registre teorias, nomes e detalhes da sua leitura. Este espaço pertence somente a você.</p>
      </div>
      <BookLoreNav bookId={bookId} active="/notes" />
      <NotesPanel bookId={bookId} />
    </div>
  );
}
