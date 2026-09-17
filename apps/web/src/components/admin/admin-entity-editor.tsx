"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { apiGet, apiWrite } from "@/lib/api";
import type { AdminEntity, AdminEntityDetail, AdminUnit } from "@/lib/admin-types";

type AliasDraft = { id: string | null; alias: string; revealAtUnitId: string };
type FactDraft = {
  id: string | null; content: string; memoryHint: string; type: string;
  importance: number; revealAtUnitId: string;
};
type Preview = { entity: { name: string }; aliases: string[]; facts: { content: string }[] };

export function AdminEntityEditor({ entityId }: { entityId: string }) {
  const router = useRouter();
  const [detail, setDetail] = useState<AdminEntityDetail | null>(null);
  const [entity, setEntity] = useState<AdminEntity | null>(null);
  const [units, setUnits] = useState<AdminUnit[]>([]);
  const [alias, setAlias] = useState<AliasDraft>({ id: null, alias: "", revealAtUnitId: "" });
  const [fact, setFact] = useState<FactDraft>({
    id: null, content: "", memoryHint: "", type: "Description",
    importance: 1, revealAtUnitId: "",
  });
  const [previewUnit, setPreviewUnit] = useState("");
  const [preview, setPreview] = useState<Preview | null>(null);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");

  async function load() {
    try {
      const data = await apiGet<AdminEntityDetail>(`/api/admin/entities/${entityId}`);
      const readingUnits = await apiGet<AdminUnit[]>(`/api/admin/books/${data.entity.bookId}/reading-units`);
      setDetail(data); setEntity(data.entity); setUnits(readingUnits);
      const first = readingUnits[0]?.id ?? "";
      setAlias((current) => ({ ...current, revealAtUnitId: current.revealAtUnitId || first }));
      setFact((current) => ({ ...current, revealAtUnitId: current.revealAtUnitId || first }));
      setPreviewUnit((current) => current || first);
    } catch (cause) { setError((cause as Error).message); }
  }
  useEffect(() => {
    apiGet<AdminEntityDetail>(`/api/admin/entities/${entityId}`)
      .then(async (data) => {
        const readingUnits = await apiGet<AdminUnit[]>(`/api/admin/books/${data.entity.bookId}/reading-units`);
        setDetail(data); setEntity(data.entity); setUnits(readingUnits);
        const first = readingUnits[0]?.id ?? "";
        setAlias((current) => ({ ...current, revealAtUnitId: current.revealAtUnitId || first }));
        setFact((current) => ({ ...current, revealAtUnitId: current.revealAtUnitId || first }));
        setPreviewUnit((current) => current || first);
      }).catch((cause) => setError((cause as Error).message));
  }, [entityId]);

  async function saveEntity(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!entity) return;
    setError(""); setMessage("");
    try {
      await apiWrite(`/api/admin/entities/${entityId}`, "PUT", entity);
      setMessage("Entidade atualizada."); await load();
    } catch (cause) { setError((cause as Error).message); }
  }

  async function saveAlias(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError(""); setMessage("");
    try {
      await apiWrite(alias.id ? `/api/admin/aliases/${alias.id}`
        : `/api/admin/entities/${entityId}/aliases`, alias.id ? "PUT" : "POST", alias);
      setAlias({ id: null, alias: "", revealAtUnitId: units[0]?.id ?? "" });
      setMessage("Alias salvo."); await load();
    } catch (cause) { setError((cause as Error).message); }
  }

  async function saveFact(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError(""); setMessage("");
    try {
      await apiWrite(fact.id ? `/api/admin/facts/${fact.id}`
        : `/api/admin/entities/${entityId}/facts`, fact.id ? "PUT" : "POST", {
        ...fact, memoryHint: fact.memoryHint || null,
      });
      setFact({ id: null, content: "", memoryHint: "", type: "Description",
        importance: 1, revealAtUnitId: units[0]?.id ?? "" });
      setMessage("Fato salvo."); await load();
    } catch (cause) { setError((cause as Error).message); }
  }

  async function remove(path: string, label: string): Promise<boolean> {
    if (!window.confirm(`Excluir ${label}?`)) return false;
    setError("");
    try { await apiWrite(path, "DELETE"); await load(); return true; }
    catch (cause) { setError((cause as Error).message); return false; }
  }

  async function showPreview() {
    setError("");
    try {
      setPreview(await apiGet<Preview>(`/api/admin/entities/${entityId}/preview?atUnitId=${previewUnit}`));
    } catch (cause) { setPreview(null); setError((cause as Error).message); }
  }

  if (!entity || !detail) return <p>{error || "Carregando entidade…"}</p>;
  return (
    <div>
      <Link href={`/admin/books/${entity.bookId}/entities`} className="text-sm underline">← Entidades</Link>
      <h1 className="mt-4 text-4xl font-semibold">{entity.name}</h1>
      {error && <p role="alert" className="mt-5 rounded-lg bg-red-50 p-3 text-red-800">{error}</p>}
      {message && <p role="status" className="mt-5 rounded-lg bg-green-50 p-3 text-green-800">{message}</p>}
      <form onSubmit={saveEntity} className="mt-8 grid max-w-2xl gap-4 rounded-2xl border bg-[var(--surface)] p-5">
        <h2 className="text-xl font-semibold">Informações iniciais</h2>
        <label className="grid gap-1 text-sm">Nome
          <input required value={entity.name} onChange={(event) => setEntity({ ...entity, name: event.target.value })} className="rounded-lg border px-3 py-2" />
        </label>
        <label className="grid gap-1 text-sm">Slug
          <input required value={entity.slug} onChange={(event) => setEntity({ ...entity, slug: event.target.value })} className="rounded-lg border px-3 py-2" />
        </label>
        <label className="grid gap-1 text-sm">Tipo
          <select value={entity.type} onChange={(event) => setEntity({ ...entity, type: event.target.value })} className="rounded-lg border px-3 py-2">
            {["Character", "Faction", "Location", "Creature", "Item", "Event", "Organization", "Concept", "Other"].map((option) => <option key={option}>{option}</option>)}
          </select>
        </label>
        <label className="grid gap-1 text-sm">Resumo seguro no primeiro contato
          <textarea rows={3} value={entity.shortDescription ?? ""} onChange={(event) => setEntity({ ...entity, shortDescription: event.target.value })} className="rounded-lg border px-3 py-2" />
        </label>
        <label className="grid gap-1 text-sm">URL da imagem inicial
          <input value={entity.imageUrl ?? ""} onChange={(event) => setEntity({ ...entity, imageUrl: event.target.value })} className="rounded-lg border px-3 py-2" />
        </label>
        <div className="grid gap-4 sm:grid-cols-3">
          <label className="grid gap-1 text-sm">Primeira conhecida em
            <select value={entity.firstKnownAtUnitId} onChange={(event) => setEntity({ ...entity, firstKnownAtUnitId: event.target.value })} className="rounded-lg border px-3 py-2">
              {units.map((unit) => <option key={unit.id} value={unit.id}>{unit.safeLabel}</option>)}
            </select>
          </label>
          <label className="grid gap-1 text-sm">Importância
            <input type="number" min="0" value={entity.importance} onChange={(event) => setEntity({ ...entity, importance: Number(event.target.value) })} className="rounded-lg border px-3 py-2" />
          </label>
          <label className="grid gap-1 text-sm">Ordem na timeline
            <input type="number" value={entity.chronologyIndex ?? ""} onChange={(event) => setEntity({ ...entity, chronologyIndex: event.target.value ? Number(event.target.value) : null })} className="rounded-lg border px-3 py-2" />
          </label>
        </div>
        <div className="flex gap-3">
          <button className="rounded-lg bg-[var(--action-surface)] px-4 py-2 text-[var(--action-text)]">Salvar entidade</button>
          <button type="button" onClick={() => {
            void remove(`/api/admin/entities/${entityId}`, "esta entidade").then((removed) => {
              if (removed) router.push(`/admin/books/${entity.bookId}/entities`);
            });
          }} className="rounded-lg border border-red-200 px-4 py-2 text-red-700">Excluir</button>
        </div>
      </form>
      <div className="mt-8 grid gap-6 lg:grid-cols-2">
        <section className="rounded-2xl border bg-[var(--surface)] p-5">
          <h2 className="text-xl font-semibold">Aliases</h2>
          <div className="mt-3 grid gap-2">
            {detail.aliases.map((item) => (
              <div key={item.id} className="flex gap-2 rounded-lg border p-3">
                <button className="flex-1 text-left" onClick={() => setAlias({ ...item, id: item.id })}>{item.alias} <small className="text-[var(--ink-soft)]">· {units.find((unit) => unit.id === item.revealAtUnitId)?.safeLabel}</small></button>
                <button onClick={() => remove(`/api/admin/aliases/${item.id}`, "este alias")} className="text-sm text-red-700">Excluir</button>
              </div>
            ))}
          </div>
          <form onSubmit={saveAlias} className="mt-5 grid gap-3">
            <label className="grid gap-1 text-sm">Alias
              <input required value={alias.alias} onChange={(event) => setAlias({ ...alias, alias: event.target.value })} className="rounded-lg border px-3 py-2" />
            </label>
            <label className="grid gap-1 text-sm">Revelado em
              <select value={alias.revealAtUnitId} onChange={(event) => setAlias({ ...alias, revealAtUnitId: event.target.value })} className="rounded-lg border px-3 py-2">
                {units.map((unit) => <option key={unit.id} value={unit.id}>{unit.safeLabel}</option>)}
              </select>
            </label>
            <button className="justify-self-start rounded-lg border px-4 py-2">{alias.id ? "Atualizar alias" : "Criar alias"}</button>
          </form>
        </section>
        <section className="rounded-2xl border bg-[var(--surface)] p-5">
          <h2 className="text-xl font-semibold">Fatos e pistas</h2>
          <div className="mt-3 grid gap-2">
            {detail.facts.map((item) => (
              <div key={item.id} className="flex gap-2 rounded-lg border p-3">
                <button className="flex-1 text-left" onClick={() => setFact({
                  ...item, id: item.id, memoryHint: item.memoryHint ?? "",
                })}>{item.content} <small className="text-[var(--ink-soft)]">· {units.find((unit) => unit.id === item.revealAtUnitId)?.safeLabel}</small></button>
                <button onClick={() => remove(`/api/admin/facts/${item.id}`, "este fato")} className="text-sm text-red-700">Excluir</button>
              </div>
            ))}
          </div>
          <form onSubmit={saveFact} className="mt-5 grid gap-3">
            <label className="grid gap-1 text-sm">Conteúdo original e curto
              <textarea required rows={3} value={fact.content} onChange={(event) => setFact({ ...fact, content: event.target.value })} className="rounded-lg border px-3 py-2" />
            </label>
            <label className="grid gap-1 text-sm">Pista de memória
              <input value={fact.memoryHint} onChange={(event) => setFact({ ...fact, memoryHint: event.target.value })} className="rounded-lg border px-3 py-2" />
            </label>
            <div className="grid gap-3 sm:grid-cols-3">
              <label className="grid gap-1 text-sm">Tipo
                <select value={fact.type} onChange={(event) => setFact({ ...fact, type: event.target.value })} className="rounded-lg border px-3 py-2">
                  {["Description", "Title", "Background", "Event", "RelationshipContext", "Other"].map((option) => <option key={option}>{option}</option>)}
                </select>
              </label>
              <label className="grid gap-1 text-sm">Importância
                <input type="number" min="0" value={fact.importance} onChange={(event) => setFact({ ...fact, importance: Number(event.target.value) })} className="rounded-lg border px-3 py-2" />
              </label>
              <label className="grid gap-1 text-sm">Revelado em
                <select value={fact.revealAtUnitId} onChange={(event) => setFact({ ...fact, revealAtUnitId: event.target.value })} className="rounded-lg border px-3 py-2">
                  {units.map((unit) => <option key={unit.id} value={unit.id}>{unit.safeLabel}</option>)}
                </select>
              </label>
            </div>
            <button className="justify-self-start rounded-lg border px-4 py-2">{fact.id ? "Atualizar fato" : "Criar fato"}</button>
          </form>
        </section>
      </div>
      <section className="mt-8 max-w-2xl rounded-2xl border bg-[var(--surface)] p-5">
        <h2 className="text-xl font-semibold">Preview do leitor</h2>
        <div className="mt-4 flex flex-wrap gap-3">
          <select aria-label="Unidade para preview" value={previewUnit} onChange={(event) => setPreviewUnit(event.target.value)} className="rounded-lg border px-3 py-2">
            {units.map((unit) => <option key={unit.id} value={unit.id}>{unit.safeLabel}</option>)}
          </select>
          <button onClick={showPreview} disabled={!previewUnit} className="rounded-lg border px-4 py-2">Visualizar</button>
        </div>
        {preview && <div className="mt-4 text-sm">
          <strong>{preview.entity.name}</strong>
          <p>Aliases: {preview.aliases.join(", ") || "nenhum"}</p>
          <p>Fatos: {preview.facts.map((item) => item.content).join(" · ") || "nenhum"}</p>
        </div>}
      </section>
    </div>
  );
}
