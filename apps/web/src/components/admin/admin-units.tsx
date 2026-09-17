"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { apiGet, apiWrite } from "@/lib/api";
import type { AdminUnit } from "@/lib/admin-types";

type UnitForm = {
  parentUnitId: string | null; title: string; safeLabel: string;
  slug: string; type: string; orderIndex: number;
};
const empty: UnitForm = {
  parentUnitId: null, title: "", safeLabel: "", slug: "",
  type: "Chapter", orderIndex: 10,
};

export function AdminUnits({ bookId }: { bookId: string }) {
  const [units, setUnits] = useState<AdminUnit[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [form, setForm] = useState<UnitForm>(empty);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const endpoint = `/api/admin/books/${bookId}/reading-units`;

  async function load() {
    try { setUnits(await apiGet<AdminUnit[]>(endpoint)); }
    catch (cause) { setError((cause as Error).message); }
  }
  useEffect(() => {
    apiGet<AdminUnit[]>(endpoint).then(setUnits)
      .catch((cause) => setError((cause as Error).message));
  }, [endpoint]);

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
      {error && <p role="alert" className="mt-5 rounded-lg bg-red-50 p-3 text-red-800">{error}</p>}
      {message && <p role="status" className="mt-5 rounded-lg bg-green-50 p-3 text-green-800">{message}</p>}
      <div className="mt-8 grid gap-6 lg:grid-cols-[1fr_1fr]">
        <div className="grid content-start gap-2">
          {units.map((unit, index) => (
            <div key={unit.id} className="flex items-center gap-2 rounded-xl border bg-[var(--surface)] p-3"
              style={{ marginLeft: unit.parentUnitId ? 20 : 0 }}>
              <button onClick={() => select(unit)} className="min-w-0 flex-1 text-left">
                <span className="block text-xs text-[var(--ink-soft)]">{unit.orderIndex} · {unit.type} · {unit.safeLabel}</span>
                <strong>{unit.title}</strong>
              </button>
              <button aria-label={`Subir ${unit.title}`} disabled={index === 0} onClick={() => move(unit.id, "up")} className="rounded border px-2 py-1 disabled:opacity-30">↑</button>
              <button aria-label={`Descer ${unit.title}`} disabled={index === units.length - 1} onClick={() => move(unit.id, "down")} className="rounded border px-2 py-1 disabled:opacity-30">↓</button>
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
            <button className="rounded-lg bg-[var(--action-surface)] px-4 py-2 text-[var(--action-text)]">Salvar</button>
            {selectedId && <button type="button" onClick={remove} className="rounded-lg border border-red-200 px-4 py-2 text-red-700">Excluir</button>}
            {selectedId && <button type="button" onClick={() => { setSelectedId(null); setForm(empty); }} className="rounded-lg border px-4 py-2">Novo</button>}
          </div>
        </form>
      </div>
    </div>
  );
}
