"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { apiGet, apiWrite } from "@/lib/api";
import type { AdminBook, AdminUnit } from "@/lib/admin-types";

type Preview = { entities: { id: string; name: string }[]; timeline: unknown[] };

export function AdminBookDetail({ bookId }: { bookId: string }) {
  const [book, setBook] = useState<AdminBook | null>(null);
  const [units, setUnits] = useState<AdminUnit[]>([]);
  const [previewUnit, setPreviewUnit] = useState("");
  const [preview, setPreview] = useState<Preview | null>(null);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  useEffect(() => {
    Promise.all([
      apiGet<AdminBook>(`/api/admin/books/${bookId}`),
      apiGet<AdminUnit[]>(`/api/admin/books/${bookId}/reading-units`),
    ]).then(([result, resultUnits]) => {
      setBook(result); setUnits(resultUnits);
      if (resultUnits[0]) setPreviewUnit(resultUnits[0].id);
    }).catch((cause) => setError((cause as Error).message));
  }, [bookId]);

  async function save(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!book) return;
    setError(""); setSuccess("");
    try {
      const updated = await apiWrite<AdminBook>(`/api/admin/books/${bookId}`, "PUT", book);
      setBook(updated); setSuccess("Livro atualizado.");
    } catch (cause) { setError((cause as Error).message); }
  }

  async function showPreview() {
    setError("");
    try {
      setPreview(await apiGet<Preview>(`/api/admin/books/${bookId}/preview?atUnitId=${previewUnit}`));
    } catch (cause) { setError((cause as Error).message); }
  }

  if (!book) return <p>Carregando livro…</p>;
  return (
    <div>
      <Link href="/admin/books" className="text-sm underline">← Livros</Link>
      <h1 className="mt-4 text-4xl font-semibold">{book.title}</h1>
      <nav aria-label="Conteúdo do livro" className="mt-6 flex flex-wrap gap-3">
        <Link className="rounded-lg border px-4 py-2" href={`/admin/books/${bookId}/reading-units`}>Unidades de leitura</Link>
        <Link className="rounded-lg border px-4 py-2" href={`/admin/books/${bookId}/entities`}>Entidades e relações</Link>
      </nav>
      {error && <p role="alert" className="mt-5 rounded-lg bg-red-50 p-3 text-red-800">{error}</p>}
      {success && <p role="status" className="mt-5 rounded-lg bg-green-50 p-3 text-green-800">{success}</p>}
      <form onSubmit={save} className="mt-8 grid max-w-2xl gap-4 rounded-2xl border bg-white p-5">
        <h2 className="text-xl font-semibold">Metadados</h2>
        <label className="grid gap-2 text-sm">Título
          <input required value={book.title} onChange={(event) => setBook({ ...book, title: event.target.value })} className="rounded-lg border px-3 py-2" />
        </label>
        <label className="grid gap-2 text-sm">Autor
          <input required value={book.author} onChange={(event) => setBook({ ...book, author: event.target.value })} className="rounded-lg border px-3 py-2" />
        </label>
        <label className="grid gap-2 text-sm">Descrição pública sem spoilers
          <textarea rows={3} value={book.description ?? ""} onChange={(event) => setBook({ ...book, description: event.target.value })} className="rounded-lg border px-3 py-2" />
        </label>
        <label className="grid gap-2 text-sm">URL da capa
          <input value={book.coverUrl ?? ""} onChange={(event) => setBook({ ...book, coverUrl: event.target.value })} className="rounded-lg border px-3 py-2" />
        </label>
        <button className="justify-self-start rounded-lg bg-[#203c35] px-4 py-2 text-white">Salvar</button>
      </form>
      <section className="mt-8 max-w-2xl rounded-2xl border bg-white p-5">
        <h2 className="text-xl font-semibold">Preview antisspoiler</h2>
        <p className="mt-2 text-sm text-[#657066]">Veja o que um leitor no ponto escolhido poderia conhecer.</p>
        <div className="mt-4 flex flex-wrap gap-3">
          <select aria-label="Unidade para preview" value={previewUnit} onChange={(event) => setPreviewUnit(event.target.value)} className="rounded-lg border px-3 py-2">
            {units.map((unit) => <option key={unit.id} value={unit.id}>{unit.safeLabel} — {unit.title}</option>)}
          </select>
          <button onClick={showPreview} disabled={!previewUnit} className="rounded-lg border px-4 py-2">Visualizar</button>
        </div>
        {preview && <p className="mt-4 text-sm">Entidades: {preview.entities.map((entity) => entity.name).join(", ") || "nenhuma"} · Eventos: {preview.timeline.length}</p>}
      </section>
    </div>
  );
}
