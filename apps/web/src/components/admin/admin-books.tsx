"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { apiGet, apiWrite } from "@/lib/api";
import type { AdminBook } from "@/lib/admin-types";

export function AdminBooks() {
  const [books, setBooks] = useState<AdminBook[]>([]);
  const [title, setTitle] = useState("");
  const [author, setAuthor] = useState("");
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);

  async function load() {
    try { setBooks(await apiGet<AdminBook[]>("/api/admin/books")); }
    catch (cause) { setError((cause as Error).message); }
  }
  useEffect(() => {
    apiGet<AdminBook[]>("/api/admin/books").then(setBooks)
      .catch((cause) => setError((cause as Error).message));
  }, []);

  async function create(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true); setError("");
    try {
      await apiWrite("/api/admin/books", "POST", { title, author });
      setTitle(""); setAuthor("");
      await load();
    } catch (cause) { setError((cause as Error).message); }
    finally { setSaving(false); }
  }

  return (
    <div>
      <p className="text-sm uppercase tracking-[.16em] text-[#657066]">Catálogo</p>
      <h1 className="mt-2 text-4xl font-semibold">Livros</h1>
      {error && <p role="alert" className="mt-5 rounded-lg bg-red-50 p-3 text-red-800">{error}</p>}
      <form onSubmit={create} className="mt-8 grid gap-3 rounded-2xl border border-[#d9ddd6] bg-white p-5 sm:grid-cols-[1fr_1fr_auto]">
        <label className="grid gap-2 text-sm">Título
          <input required value={title} onChange={(event) => setTitle(event.target.value)} className="rounded-lg border border-[#cbd1c9] px-3 py-2" />
        </label>
        <label className="grid gap-2 text-sm">Autor
          <input required value={author} onChange={(event) => setAuthor(event.target.value)} className="rounded-lg border border-[#cbd1c9] px-3 py-2" />
        </label>
        <button disabled={saving} className="self-end rounded-lg bg-[#203c35] px-4 py-2.5 text-white disabled:opacity-50">Criar livro</button>
      </form>
      <div className="mt-8 grid gap-3">
        {books.map((book) => (
          <Link key={book.id} href={`/admin/books/${book.id}`}
            className="rounded-xl border border-[#d9ddd6] bg-white p-5 hover:border-[#748d79]">
            <strong className="block text-lg">{book.title}</strong>
            <span className="text-sm text-[#657066]">{book.author}</span>
          </Link>
        ))}
        {books.length === 0 && <p className="text-[#657066]">Nenhum livro no catálogo.</p>}
      </div>
    </div>
  );
}
