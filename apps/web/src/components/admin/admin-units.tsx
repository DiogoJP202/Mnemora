"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { apiGet, apiWrite } from "@/lib/api";
import type { AdminBook, AdminUnit } from "@/lib/admin-types";

type UnitForm = {
  parentUnitId: string | null; title: string; safeLabel: string;
  slug: string; type: string; orderIndex: number;
};
type ExternalOutline = {
  provider: string;
  externalId: string;
  sourceUrl: string;
  attribution: string;
  items: { title: string; level: number; page: number | null }[];
};
const empty: UnitForm = {
  parentUnitId: null, title: "", safeLabel: "", slug: "",
  type: "Chapter", orderIndex: 10,
};

export function AdminUnits({ bookId }: { bookId: string }) {
  const [book, setBook] = useState<AdminBook | null>(null);
  const [units, setUnits] = useState<AdminUnit[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [form, setForm] = useState<UnitForm>(empty);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [bulkText, setBulkText] = useState("");
  const [bulkSaving, setBulkSaving] = useState(false);
  const [sourceOutline, setSourceOutline] = useState<ExternalOutline | null>(null);
  const [sourceLoading, setSourceLoading] = useState(false);
  const endpoint = `/api/admin/books/${bookId}/reading-units`;

  async function load() {
    try { setUnits(await apiGet<AdminUnit[]>(endpoint)); }
    catch (cause) { setError((cause as Error).message); }
  }
  useEffect(() => {
    Promise.all([
      apiGet<AdminUnit[]>(endpoint),
      apiGet<AdminBook>(`/api/admin/books/${bookId}`),
    ]).then(([resultUnits, resultBook]) => {
      setUnits(resultUnits);
      setBook(resultBook);
    }).catch((cause) => setError((cause as Error).message));
  }, [bookId, endpoint]);

  const bulkTitles = bulkText.split(/\r?\n/)
    .map((title) => title.trim())
    .filter(Boolean)
    .slice(0, 200);
  const editable = book?.readingOutlineStatus === "Draft";

  function slugify(value: string, index: number) {
    const slug = value.normalize("NFD").replace(/[\u0300-\u036f]/g, "")
      .toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, "");
    return `${slug || "capitulo"}-${index}`;
  }

  async function createBulk() {
    if (!editable || bulkTitles.length === 0) return;
    setBulkSaving(true);
    setError("");
    setMessage("");
    const baseOrder = units.length === 0
      ? 0
      : Math.max(...units.map((unit) => unit.orderIndex));
    try {
      for (let index = 0; index < bulkTitles.length; index += 1) {
        const number = units.length + index + 1;
        await apiWrite(endpoint, "POST", {
          parentUnitId: null,
          title: bulkTitles[index],
          safeLabel: `Capítulo ${number}`,
          slug: slugify(bulkTitles[index], number),
          type: "Chapter",
          orderIndex: baseOrder + (index + 1) * 10,
        });
      }
      setMessage(`${bulkTitles.length} ${bulkTitles.length === 1 ? "unidade criada" : "unidades criadas"}. Revise títulos, rótulos e hierarquia antes de publicar.`);
      setBulkText("");
      await load();
    } catch (cause) {
      setError((cause as Error).message);
      await load();
    } finally {
      setBulkSaving(false);
    }
  }

  async function findSourceOutline() {
    setSourceLoading(true);
    setError("");
    setMessage("");
    try {
      setSourceOutline(await apiGet<ExternalOutline>(
        `/api/admin/books/${bookId}/reading-outline/source`,
      ));
    } catch (cause) {
      setSourceOutline(null);
      setError((cause as Error).message);
    } finally {
      setSourceLoading(false);
    }
  }

  async function importSourceOutline() {
    if (!sourceOutline) return;
    setSourceLoading(true);
    setError("");
    try {
      await apiWrite(`/api/admin/books/${bookId}/reading-outline/import`, "POST", {
        sourceUrl: sourceOutline.sourceUrl,
        attribution: sourceOutline.attribution,
        items: sourceOutline.items,
      });
      setMessage(`${sourceOutline.items.length} itens da edição foram importados como rascunho. Revise a estrutura antes de publicar.`);
      setSourceOutline(null);
      await load();
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setSourceLoading(false);
    }
  }

  function select(unit: AdminUnit) {
    setSelectedId(unit.id);
    setForm({
      parentUnitId: unit.parentUnitId, title: unit.title,
      safeLabel: unit.safeLabel, slug: unit.slug,
      type: unit.type, orderIndex: unit.orderIndex,
    });
    setMessage("");
  }

  async function save(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError(""); setMessage("");
    try {
      await apiWrite(selectedId ? `${endpoint}/${selectedId}` : endpoint,
        selectedId ? "PUT" : "POST", form);
      setMessage(selectedId ? "Unidade atualizada." : "Unidade criada.");
      setSelectedId(null); setForm(empty); await load();
    } catch (cause) { setError((cause as Error).message); }
  }

  async function move(id: string, direction: "up" | "down") {
    setError("");
    try { await apiWrite(`${endpoint}/${id}/move?direction=${direction}`, "POST"); await load(); }
    catch (cause) { setError((cause as Error).message); }
  }

  async function remove() {
    if (!selectedId || !window.confirm("Excluir esta unidade de leitura?")) return;
    setError("");
    try {
      await apiWrite(`${endpoint}/${selectedId}`, "DELETE");
      setSelectedId(null); setForm(empty); await load();
    } catch (cause) { setError((cause as Error).message); }
  }

  return (
    <div>
      <Link href={`/admin/books/${bookId}`} className="text-sm underline">← Livro</Link>
      <h1 className="mt-4 text-4xl font-semibold">Unidades de leitura</h1>
      <p className="mt-3 max-w-2xl text-[var(--ink-soft)]">A ordem é absoluta por livro. O rótulo público precisa ser seguro mesmo antes de o leitor alcançar a unidade.</p>
      {book && !editable && (
        <p className="mt-5 rounded-lg border border-[var(--line)] bg-[var(--surface)] p-4" role="status">
          {book.readingOutlineStatus === "Published"
            ? "O índice está publicado. Volte ao livro e escolha “Editar índice” antes de alterar unidades."
            : "Inicie a preparação do pacote no detalhe do livro antes de cadastrar unidades."}
        </p>
      )}
      {error && <p role="alert" className="mt-5 rounded-lg bg-red-50 p-3 text-red-800">{error}</p>}
      {message && <p role="status" className="mt-5 rounded-lg bg-green-50 p-3 text-green-800">{message}</p>}
      {editable && units.length === 0 && book?.externalProvider === "OpenLibrary" && book.externalId?.toUpperCase().endsWith("M") && (
        <section className="mt-8 rounded-2xl border bg-[var(--surface)] p-5" aria-labelledby="source-outline-title">
          <span className="section-index">SUMÁRIO DA EDIÇÃO</span>
          <h2 id="source-outline-title" className="mt-2 text-xl font-semibold">Verificar a Open Library</h2>
          <p className="mt-2 max-w-3xl text-sm text-[var(--ink-soft)]">
            A consulta usa somente a edição exata <strong>{book.externalId}</strong>. O sumário é opcional no catálogo e sempre entra como rascunho para revisão humana.
          </p>
          {!sourceOutline ? (
            <button type="button" onClick={() => void findSourceOutline()} disabled={sourceLoading} className="mt-4 rounded-lg border px-4 py-2 disabled:opacity-50">
              {sourceLoading ? "Consultando…" : "Buscar sumário desta edição"}
            </button>
          ) : (
            <div className="mt-5">
              <p className="text-sm"><strong>{sourceOutline.items.length} itens encontrados</strong> · fonte {sourceOutline.attribution}</p>
              <ol className="mt-3 max-h-72 overflow-auto rounded-xl border border-[var(--line)] p-4 text-sm">
                {sourceOutline.items.map((item, index) => (
                  <li key={`${item.title}-${index}`} style={{ marginLeft: `${Math.max(0, item.level - 1) * 16}px` }}>
                    <span className="text-[var(--ink-soft)]">{index + 1}.</span> {item.title}{item.page ? ` · p. ${item.page}` : ""}
                  </li>
                ))}
              </ol>
              <p className="mt-3 text-xs text-[var(--ink-soft)]">
                Os títulos não chegam ao leitor até a publicação; unidades futuras usarão rótulos neutros.
              </p>
              <div className="mt-4 flex flex-wrap gap-2">
                <button type="button" onClick={() => void importSourceOutline()} disabled={sourceLoading} className="rounded-lg bg-[var(--action-surface)] px-4 py-2 text-[var(--action-text)] disabled:opacity-50">
                  {sourceLoading ? "Importando…" : "Importar como rascunho"}
                </button>
                <button type="button" onClick={() => setSourceOutline(null)} disabled={sourceLoading} className="rounded-lg border px-4 py-2">Descartar prévia</button>
              </div>
            </div>
          )}
        </section>
      )}
      {editable && (
        <section className="mt-8 rounded-2xl border bg-[var(--surface)] p-5" aria-labelledby="bulk-units-title">
          <span className="section-index">CADASTRO EM LOTE</span>
          <h2 id="bulk-units-title" className="mt-2 text-xl font-semibold">Cole um sumário revisável</h2>
          <p className="mt-2 max-w-3xl text-sm text-[var(--ink-soft)]">
            Use uma linha por capítulo. O Mnemora sugere ordem, slug e rótulos neutros; revise tudo antes de publicar. Não use uma sinopse como sumário.
          </p>
          <label className="mt-4 grid gap-2 text-sm">Títulos das unidades
            <textarea rows={7} value={bulkText} onChange={(event) => setBulkText(event.target.value)} placeholder={"A chegada\nA primeira travessia\nO mapa"} className="rounded-lg border px-3 py-2" />
          </label>
          {bulkTitles.length > 0 && (
            <div className="mt-4 rounded-xl border border-[var(--line)] p-4">
              <strong className="text-sm">Prévia de {bulkTitles.length} {bulkTitles.length === 1 ? "unidade" : "unidades"}</strong>
              <ol className="mt-3 grid gap-1 text-sm text-[var(--ink-soft)]">
                {bulkTitles.slice(0, 8).map((title, index) => (
                  <li key={`${title}-${index}`}>Capítulo {units.length + index + 1} — {title}</li>
                ))}
                {bulkTitles.length > 8 && <li>…e mais {bulkTitles.length - 8}</li>}
              </ol>
            </div>
          )}
          <button type="button" onClick={() => void createBulk()} disabled={bulkSaving || bulkTitles.length === 0} className="mt-4 rounded-lg bg-[var(--action-surface)] px-4 py-2 text-[var(--action-text)] disabled:opacity-50">
            {bulkSaving ? "Criando unidades…" : "Criar como rascunho"}
          </button>
        </section>
      )}
      <div className="mt-8 grid gap-6 lg:grid-cols-[1fr_1fr]">
        <div className="grid content-start gap-2">
          {units.map((unit, index) => (
            <div key={unit.id} className="flex items-center gap-2 rounded-xl border bg-[var(--surface)] p-3"
              style={{ marginLeft: unit.parentUnitId ? 20 : 0 }}>
              <button onClick={() => select(unit)} className="min-w-0 flex-1 text-left">
                <span className="block text-xs text-[var(--ink-soft)]">{unit.orderIndex} · {unit.type} · {unit.safeLabel}</span>
                <strong>{unit.title}</strong>
              </button>
              <button aria-label={`Subir ${unit.title}`} disabled={!editable || index === 0} onClick={() => move(unit.id, "up")} className="rounded border px-2 py-1 disabled:opacity-30">↑</button>
              <button aria-label={`Descer ${unit.title}`} disabled={!editable || index === units.length - 1} onClick={() => move(unit.id, "down")} className="rounded border px-2 py-1 disabled:opacity-30">↓</button>
            </div>
          ))}
          {units.length === 0 && <p>Nenhuma unidade criada.</p>}
        </div>
        <form onSubmit={save} className="grid content-start gap-4 rounded-2xl border bg-[var(--surface)] p-5">
          <h2 className="text-xl font-semibold">{selectedId ? "Editar unidade" : "Nova unidade"}</h2>
          <label className="grid gap-1 text-sm">Título completo
            <input required value={form.title} onChange={(event) => setForm({ ...form, title: event.target.value })} className="rounded-lg border px-3 py-2" />
          </label>
          <label className="grid gap-1 text-sm">Rótulo seguro
            <input required value={form.safeLabel} onChange={(event) => setForm({ ...form, safeLabel: event.target.value })} placeholder="Ex.: Capítulo 2" className="rounded-lg border px-3 py-2" />
          </label>
          <label className="grid gap-1 text-sm">Slug
            <input required value={form.slug} onChange={(event) => setForm({ ...form, slug: event.target.value })} className="rounded-lg border px-3 py-2" />
          </label>
          <div className="grid gap-4 sm:grid-cols-2">
            <label className="grid gap-1 text-sm">Tipo
              <select value={form.type} onChange={(event) => setForm({ ...form, type: event.target.value })} className="rounded-lg border px-3 py-2">
                {["Part", "Chapter", "Section", "Subsection"].map((type) => <option key={type}>{type}</option>)}
              </select>
            </label>
            <label className="grid gap-1 text-sm">Ordem
              <input required type="number" min="1" value={form.orderIndex} onChange={(event) => setForm({ ...form, orderIndex: Number(event.target.value) })} className="rounded-lg border px-3 py-2" />
            </label>
          </div>
          <label className="grid gap-1 text-sm">Unidade pai
            <select value={form.parentUnitId ?? ""} onChange={(event) => setForm({ ...form, parentUnitId: event.target.value || null })} className="rounded-lg border px-3 py-2">
              <option value="">Nenhuma</option>
              {units.filter((unit) => unit.id !== selectedId).map((unit) => <option key={unit.id} value={unit.id}>{unit.safeLabel}</option>)}
            </select>
          </label>
          <div className="flex flex-wrap gap-2">
            <button disabled={!editable} className="rounded-lg bg-[var(--action-surface)] px-4 py-2 text-[var(--action-text)] disabled:opacity-50">Salvar</button>
            {selectedId && <button type="button" disabled={!editable} onClick={remove} className="rounded-lg border border-red-200 px-4 py-2 text-red-700 disabled:opacity-50">Excluir</button>}
            {selectedId && <button type="button" onClick={() => { setSelectedId(null); setForm(empty); }} className="rounded-lg border px-4 py-2">Novo</button>}
          </div>
        </form>
      </div>
    </div>
  );
}
