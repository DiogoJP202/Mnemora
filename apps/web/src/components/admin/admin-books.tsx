"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { apiGet, apiWrite } from "@/lib/api";
import type { AdminBook } from "@/lib/admin-types";

type Filter = "all" | "requested" | "preparing" | "available" | "empty";

function state(book: AdminBook) {
  if (book.readingOutlineStatus === "Published" && book.loreStatus === "Published") {
    return { key: "available", label: "Memória publicada" };
  }
  if (book.readingOutlineStatus === "Draft" || book.loreStatus === "Draft") {
    return { key: "preparing", label: "Em preparação" };
  }
  if (book.requestCount > 0) return { key: "requested", label: "Solicitado" };
  return { key: "empty", label: "Sem pack" };
}

export function AdminBooks() {
  const [books, setBooks] = useState<AdminBook[]>([]);
  const [title, setTitle] = useState("");
  const [author, setAuthor] = useState("");
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState<Filter>("all");
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
    setSaving(true);
    setError("");
    try {
      await apiWrite("/api/admin/books", "POST", { title, author });
      setTitle("");
      setAuthor("");
      await load();
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setSaving(false);
    }
  }

  const visible = useMemo(() => {
    const normalized = query.trim().toLocaleLowerCase("pt-BR");
    return books.filter((book) => {
      const packState = state(book).key;
      if (filter !== "all" && packState !== filter) return false;
      if (!normalized) return true;
      return [book.title, book.author, book.isbn10, book.isbn13,
        book.externalProvider, book.externalId]
        .some((value) => value?.toLocaleLowerCase("pt-BR").includes(normalized));
    });
  }, [books, filter, query]);

  return (
    <div>
      <p className="text-sm uppercase tracking-[.16em] text-[var(--ink-soft)]">
        Fila editorial
      </p>
      <h1 className="mt-2 text-4xl font-semibold">Pacotes de memória</h1>
      <p className="mt-3 max-w-3xl text-[var(--ink-soft)]">
        Livros importados entram nesta fila sem lore. Prepare o índice, revise cada revelação e publique somente quando o conteúdo estiver completo.
      </p>

      {error && (
        <p role="alert" className="mt-5 rounded-lg bg-red-50 p-3 text-red-800">
          {error}
        </p>
      )}

      <form onSubmit={create} className="mt-8 grid gap-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5 sm:grid-cols-[1fr_1fr_auto]">
        <label className="grid gap-2 text-sm">Título
          <input required value={title} onChange={(event) => setTitle(event.target.value)} className="rounded-lg border border-[var(--line)] px-3 py-2" />
        </label>
        <label className="grid gap-2 text-sm">Autor
          <input required value={author} onChange={(event) => setAuthor(event.target.value)} className="rounded-lg border border-[var(--line)] px-3 py-2" />
        </label>
        <button disabled={saving} className="self-end rounded-lg bg-[var(--action-surface)] px-4 py-2.5 text-[var(--action-text)] disabled:opacity-50">
          {saving ? "Criando…" : "Criar livro curado"}
        </button>
      </form>

      <div className="mt-8 grid gap-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-4 md:grid-cols-[1fr_auto]">
        <label className="grid gap-2 text-sm">Buscar na fila
          <input
            type="search"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Título, autor, ISBN ou ID externo"
            className="rounded-lg border border-[var(--line)] px-3 py-2"
          />
        </label>
        <label className="grid gap-2 text-sm">Estado
          <select value={filter} onChange={(event) => setFilter(event.target.value as Filter)} className="rounded-lg border border-[var(--line)] px-3 py-2">
            <option value="all">Todos</option>
            <option value="requested">Solicitados</option>
            <option value="preparing">Em preparação</option>
            <option value="available">Publicados</option>
            <option value="empty">Sem pack</option>
          </select>
        </label>
      </div>

      <p className="mt-5 text-sm text-[var(--ink-soft)]" role="status">
        {visible.length} {visible.length === 1 ? "livro" : "livros"} nesta visão
      </p>

      <div className="mt-4 grid gap-3">
        {visible.map((book) => {
          const packState = state(book);
          return (
            <Link key={book.id} href={`/admin/books/${book.id}`}
              className="admin-pack-row rounded-xl border border-[var(--line)] bg-[var(--surface)] p-5 hover:border-[var(--olive)]">
              <div>
                <div className="flex flex-wrap gap-2 text-xs uppercase tracking-[.12em] text-[var(--ink-soft)]">
                  <span>{book.catalogKind === "Imported" ? "Importado" : "Curado"}</span>
                  <span>•</span>
                  <span>{packState.label}</span>
                  {book.requestCount > 0 && <span className="admin-request-count">{book.requestCount} {book.requestCount === 1 ? "pedido" : "pedidos"}</span>}
                </div>
                <strong className="mt-2 block text-lg">{book.title}</strong>
                <span className="text-sm text-[var(--ink-soft)]">{book.author}</span>
                {(book.isbn13 || book.isbn10) && (
                  <span className="mt-1 block text-xs text-[var(--ink-soft)]">
                    ISBN {book.isbn13 ?? book.isbn10}
                  </span>
                )}
              </div>
              <dl className="admin-pack-counts" aria-label="Conteúdo cadastrado">
                <div><dt>Unidades</dt><dd>{book.unitCount}</dd></div>
                <div><dt>Entidades</dt><dd>{book.entityCount}</dd></div>
                <div><dt>Fatos</dt><dd>{book.factCount}</dd></div>
                <div><dt>Relações</dt><dd>{book.relationCount}</dd></div>
                <div><dt>Eventos</dt><dd>{book.eventCount}</dd></div>
              </dl>
            </Link>
          );
        })}
        {visible.length === 0 && (
          <p className="rounded-xl border border-dashed border-[var(--line)] p-6 text-[var(--ink-soft)]">
            Nenhum livro corresponde aos filtros.
          </p>
        )}
      </div>
    </div>
  );
}
