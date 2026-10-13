"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { apiGet, apiWrite } from "@/lib/api";
import type { AdminBook, AdminUnit } from "@/lib/admin-types";
import { AdminMemoryPackTransfer } from "@/components/admin/admin-memory-pack-transfer";

type Preview = { entities: { id: string; name: string }[]; timeline: unknown[] };
type Action = "prepare" | "publish-outline" | "publish-lore" | "unpublish-lore" | "unpublish-outline";

const actionMessages: Record<Action, string> = {
  prepare: "O pacote entrou em preparação.",
  "publish-outline": "As unidades de leitura foram publicadas.",
  "publish-lore": "O pacote de memória foi publicado.",
  "unpublish-lore": "O conhecimento voltou para rascunho e deixou de aparecer ao leitor.",
  "unpublish-outline": "O índice voltou para rascunho; o conhecimento também foi retirado.",
};

export function AdminBookDetail({ bookId }: { bookId: string }) {
  const [book, setBook] = useState<AdminBook | null>(null);
  const [savedBook, setSavedBook] = useState<AdminBook | null>(null);
  const [units, setUnits] = useState<AdminUnit[]>([]);
  const [previewUnit, setPreviewUnit] = useState("");
  const [preview, setPreview] = useState<Preview | null>(null);
  const [busy, setBusy] = useState<Action | "save" | null>(null);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [transferBusy, setTransferBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      apiGet<AdminBook>(`/api/admin/books/${bookId}`),
      apiGet<AdminUnit[]>(`/api/admin/books/${bookId}/reading-units`),
    ]).then(([result, resultUnits]) => {
      if (cancelled) return;
      setBook(result);
      setSavedBook(result);
      setUnits(resultUnits);
      setPreviewUnit(resultUnits[0]?.id || "");
    }).catch((cause) => { if (!cancelled) setError((cause as Error).message); });
    return () => { cancelled = true; };
  }, [bookId]);

  async function refreshContent() {
    try {
      const [updated, updatedUnits] = await Promise.all([
        apiGet<AdminBook>(`/api/admin/books/${bookId}`),
        apiGet<AdminUnit[]>(`/api/admin/books/${bookId}/reading-units`),
      ]);
      setBook(updated);
      setSavedBook(updated);
      setUnits(updatedUnits);
      setPreviewUnit(updatedUnits[0]?.id ?? "");
      setPreview(null);
    } catch (cause) {
      setError(`A importação foi concluída, mas não foi possível atualizar a tela: ${(cause as Error).message}`);
    }
  }

  async function save(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!book) return;
    setBusy("save");
    setError("");
    setSuccess("");
    try {
      const updated = await apiWrite<AdminBook>(`/api/admin/books/${bookId}`, "PUT", {
        title: book.title,
        author: book.author,
        description: book.description,
        coverUrl: book.coverUrl,
      });
      setBook(updated);
      setSavedBook(updated);
      setSuccess("Metadados atualizados.");
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setBusy(null);
    }
  }

  async function transition(action: Action) {
    setBusy(action);
    setError("");
    setSuccess("");
    try {
      const updated = await apiWrite<AdminBook>(
        `/api/admin/books/${bookId}/memory-pack/${action}`,
        "POST",
      );
      setBook(updated);
      setSavedBook(updated);
      setSuccess(actionMessages[action]);
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setBusy(null);
    }
  }

  async function showPreview() {
    setError("");
    try {
      setPreview(await apiGet<Preview>(
        `/api/admin/books/${bookId}/preview?atUnitId=${previewUnit}`,
      ));
    } catch (cause) {
      setError((cause as Error).message);
    }
  }

  if (!book || book.id !== bookId) return error ? <p role="alert">{error}</p> : <p>Carregando livro…</p>;
  const isBusy = busy !== null || transferBusy;
  const hasUnsavedMetadata = Boolean(savedBook && (
    savedBook.title !== book.title || savedBook.author !== book.author
    || savedBook.description !== book.description || savedBook.coverUrl !== book.coverUrl
  ));
  const needsPreparation = book.readingOutlineStatus === "None" || book.loreStatus === "None";
  const editionRows = [
    ["Origem", book.catalogKind === "Imported" ? "Catálogo importado" : "Catálogo curado"],
    ["Provedor", book.externalProvider],
    ["ID externo", book.externalId],
    ["ISBN", book.isbn13 ?? book.isbn10],
    ["Editora", book.publisher],
    ["Publicação", book.publishedDate?.slice(0, 4)],
    ["Idioma", book.language],
    ["Fonte do índice", book.readingOutlineAttribution],
  ].filter((row): row is [string, string] => Boolean(row[1]));

  return (
    <div>
      <Link href="/admin/books" className="text-sm underline">← Pacotes de memória</Link>
      <div className="mt-4 flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="text-sm uppercase tracking-[.16em] text-[var(--ink-soft)]">
            {book.catalogKind === "Imported" ? "Edição importada" : "Livro curado"}
          </p>
          <h1 className="mt-2 text-4xl font-semibold">{book.title}</h1>
          <p className="mt-2 text-[var(--ink-soft)]">{book.author}</p>
        </div>
        {book.requestCount > 0 && (
          <span className="admin-request-count rounded-full px-4 py-2 text-sm">
            {book.requestCount} {book.requestCount === 1 ? "leitor pediu" : "leitores pediram"} este pack
          </span>
        )}
      </div>

      <nav aria-label="Conteúdo do livro" className="mt-6 flex flex-wrap gap-3">
        <Link className="rounded-lg border px-4 py-2" href={`/admin/books/${bookId}/reading-units`}>
          Unidades de leitura ({book.unitCount})
        </Link>
        <Link className="rounded-lg border px-4 py-2" href={`/admin/books/${bookId}/entities`}>
          Conhecimento ({book.entityCount})
        </Link>
      </nav>

      {error && <p role="alert" className="mt-5 rounded-lg bg-red-50 p-3 text-red-800">{error}</p>}
      {success && <p role="status" className="mt-5 rounded-lg bg-green-50 p-3 text-green-800">{success}</p>}

      <section className="admin-workflow mt-8 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5" aria-labelledby="workflow-title">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <span className="section-index">FLUXO EDITORIAL</span>
            <h2 id="workflow-title" className="mt-2 text-2xl font-semibold">Preparar sem vazar rascunhos</h2>
            <p className="mt-2 max-w-2xl text-sm text-[var(--ink-soft)]">
              O índice pode ser publicado primeiro. O conhecimento só abre personagens, facções, lugares, timeline, Recall e revisão depois da validação final.
            </p>
          </div>
          {needsPreparation && (
            <button type="button" className="rounded-lg bg-[var(--action-surface)] px-4 py-2 text-[var(--action-text)] disabled:opacity-50" disabled={isBusy} onClick={() => void transition("prepare")}>
              {busy === "prepare" ? "Preparando…" : "Iniciar preparação"}
            </button>
          )}
        </div>

        <ol className="admin-workflow-steps mt-6">
          <li className={book.readingOutlineStatus === "Published" ? "is-published" : ""}>
            <div><span>1</span><strong>Unidades de leitura</strong></div>
            <p>{book.unitCount} cadastradas · estado {book.readingOutlineStatus === "Published" ? "publicado" : book.readingOutlineStatus === "Draft" ? "rascunho" : "não iniciado"}</p>
            <div className="flex flex-wrap gap-2">
              {book.readingOutlineStatus === "Draft" && (
                <button type="button" className="rounded-lg bg-[var(--action-surface)] px-3 py-2 text-sm text-[var(--action-text)]" disabled={isBusy} onClick={() => void transition("publish-outline")}>Publicar índice</button>
              )}
              {book.readingOutlineStatus === "Published" && (
                <button type="button" className="rounded-lg border px-3 py-2 text-sm" disabled={isBusy} onClick={() => void transition("unpublish-outline")}>Editar índice</button>
              )}
            </div>
          </li>
          <li className={book.loreStatus === "Published" ? "is-published" : ""}>
            <div><span>2</span><strong>Conhecimento antisspoiler</strong></div>
            <p>{book.entityCount} entidades · {book.factCount} fatos · {book.relationCount} relações · {book.eventCount} eventos</p>
            <div className="flex flex-wrap gap-2">
              {book.loreStatus === "Draft" && book.readingOutlineStatus === "Published" && (
                <button type="button" className="rounded-lg bg-[var(--action-surface)] px-3 py-2 text-sm text-[var(--action-text)]" disabled={isBusy} onClick={() => void transition("publish-lore")}>Publicar conhecimento</button>
              )}
              {book.loreStatus === "Published" && (
                <button type="button" className="rounded-lg border px-3 py-2 text-sm" disabled={isBusy} onClick={() => void transition("unpublish-lore")}>Editar conhecimento</button>
              )}
            </div>
          </li>
        </ol>
      </section>

      {editionRows.length > 0 && (
        <section className="mt-8 max-w-4xl rounded-2xl border bg-[var(--surface)] p-5">
          <h2 className="text-xl font-semibold">Identidade desta edição</h2>
          <p className="mt-2 text-sm text-[var(--ink-soft)]">
            Confirme a edição antes de cadastrar capítulos. Traduções e versões abreviadas podem ter sequências diferentes.
          </p>
          <dl className="admin-edition-grid mt-4">
            {editionRows.map(([label, value]) => (
              <div key={label}><dt>{label}</dt><dd>{value}</dd></div>
            ))}
          </dl>
        </section>
      )}

      <AdminMemoryPackTransfer
        key={`${bookId}:${savedBook?.title}:${savedBook?.author}`}
        book={savedBook ?? book}
        disabled={busy !== null || hasUnsavedMetadata}
        disabledReason={hasUnsavedMetadata ? "Salve os metadados antes de importar ou exportar um pacote." : undefined}
        onWorkingChange={setTransferBusy}
        onImported={refreshContent}
      />

      <form onSubmit={save} className="mt-8 grid max-w-4xl gap-4 rounded-2xl border bg-[var(--surface)] p-5">
        <h2 className="text-xl font-semibold">Metadados públicos</h2>
        <label className="grid gap-2 text-sm">Título
          <input required disabled={isBusy} value={book.title} onChange={(event) => setBook({ ...book, title: event.target.value })} className="rounded-lg border px-3 py-2" />
        </label>
        <label className="grid gap-2 text-sm">Autor
          <input required disabled={isBusy} value={book.author} onChange={(event) => setBook({ ...book, author: event.target.value })} className="rounded-lg border px-3 py-2" />
        </label>
        <label className="grid gap-2 text-sm">Descrição pública sem spoilers
          <textarea rows={3} disabled={isBusy} value={book.description ?? ""} onChange={(event) => setBook({ ...book, description: event.target.value })} className="rounded-lg border px-3 py-2" />
        </label>
        <label className="grid gap-2 text-sm">URL da capa
          <input disabled={isBusy} value={book.coverUrl ?? ""} onChange={(event) => setBook({ ...book, coverUrl: event.target.value })} className="rounded-lg border px-3 py-2" />
        </label>
        <button disabled={isBusy} className="justify-self-start rounded-lg bg-[var(--action-surface)] px-4 py-2 text-[var(--action-text)] disabled:opacity-50">
          {busy === "save" ? "Salvando…" : "Salvar metadados"}
        </button>
      </form>

      <section className="mt-8 max-w-4xl rounded-2xl border bg-[var(--surface)] p-5">
        <h2 className="text-xl font-semibold">Preview antisspoiler</h2>
        <p className="mt-2 text-sm text-[var(--ink-soft)]">
          Veja o que um leitor no ponto escolhido poderia conhecer, inclusive enquanto o pacote ainda está em rascunho.
        </p>
        <div className="mt-4 flex flex-wrap gap-3">
          <select aria-label="Unidade para preview" value={previewUnit} onChange={(event) => setPreviewUnit(event.target.value)} className="rounded-lg border px-3 py-2">
            {units.map((unit) => <option key={unit.id} value={unit.id}>{unit.safeLabel} — {unit.title}</option>)}
          </select>
          <button type="button" onClick={() => void showPreview()} disabled={!previewUnit || isBusy} className="rounded-lg border px-4 py-2 disabled:opacity-50">Visualizar</button>
        </div>
        {preview && (
          <p className="mt-4 text-sm">
            Entidades: {preview.entities.map((entity) => entity.name).join(", ") || "nenhuma"} · Eventos: {preview.timeline.length}
          </p>
        )}
      </section>
    </div>
  );
}
