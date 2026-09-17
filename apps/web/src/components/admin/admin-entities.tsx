"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { apiGet, apiWrite } from "@/lib/api";
import type { AdminEntity, AdminRelation, AdminUnit } from "@/lib/admin-types";

export function AdminEntities({ bookId }: { bookId: string }) {
  const [entities, setEntities] = useState<AdminEntity[]>([]);
  const [units, setUnits] = useState<AdminUnit[]>([]);
  const [relations, setRelations] = useState<AdminRelation[]>([]);
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [type, setType] = useState("Character");
  const [summary, setSummary] = useState("");
  const [firstUnit, setFirstUnit] = useState("");
  const [sourceId, setSourceId] = useState("");
  const [targetId, setTargetId] = useState("");
  const [relationType, setRelationType] = useState("Knows");
  const [relationLabel, setRelationLabel] = useState("");
  const [revealUnit, setRevealUnit] = useState("");
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");

  async function load() {
    try {
      const [known, readingUnits, links] = await Promise.all([
        apiGet<AdminEntity[]>(`/api/admin/books/${bookId}/entities`),
        apiGet<AdminUnit[]>(`/api/admin/books/${bookId}/reading-units`),
        apiGet<AdminRelation[]>(`/api/admin/books/${bookId}/relations`),
      ]);
      setEntities(known); setUnits(readingUnits); setRelations(links);
      if (!firstUnit && readingUnits[0]) setFirstUnit(readingUnits[0].id);
      if (!revealUnit && readingUnits[0]) setRevealUnit(readingUnits[0].id);
      if (!sourceId && known[0]) setSourceId(known[0].id);
      if (!targetId && known[1]) setTargetId(known[1].id);
    } catch (cause) { setError((cause as Error).message); }
  }
  useEffect(() => {
    Promise.all([
      apiGet<AdminEntity[]>(`/api/admin/books/${bookId}/entities`),
      apiGet<AdminUnit[]>(`/api/admin/books/${bookId}/reading-units`),
      apiGet<AdminRelation[]>(`/api/admin/books/${bookId}/relations`),
    ]).then(([known, readingUnits, links]) => {
      setEntities(known); setUnits(readingUnits); setRelations(links);
      setFirstUnit((current) => current || readingUnits[0]?.id || "");
      setRevealUnit((current) => current || readingUnits[0]?.id || "");
      setSourceId((current) => current || known[0]?.id || "");
      setTargetId((current) => current || known[1]?.id || "");
    }).catch((cause) => setError((cause as Error).message));
  }, [bookId]);

  async function createEntity(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError(""); setMessage("");
    try {
      await apiWrite(`/api/admin/books/${bookId}/entities`, "POST", {
        type, name, slug, shortDescription: summary || null, imageUrl: null,
        firstKnownAtUnitId: firstUnit, importance: 1, chronologyIndex: null,
      });
      setName(""); setSlug(""); setSummary(""); setMessage("Entidade criada.");
      await load();
    } catch (cause) { setError((cause as Error).message); }
  }

  async function createRelation(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError(""); setMessage("");
    try {
      await apiWrite(`/api/admin/books/${bookId}/relations`, "POST", {
        sourceEntityId: sourceId, targetEntityId: targetId,
        relationType, label: relationLabel || null, revealAtUnitId: revealUnit,
      });
      setMessage("Relação criada."); await load();
    } catch (cause) { setError((cause as Error).message); }
  }

  async function removeRelation(id: string) {
    if (!window.confirm("Excluir esta relação?")) return;
    setError("");
    try { await apiWrite(`/api/admin/relations/${id}`, "DELETE"); await load(); }
    catch (cause) { setError((cause as Error).message); }
  }

  const names = new Map(entities.map((entity) => [entity.id, entity.name]));
  return (
    <div>
      <Link href={`/admin/books/${bookId}`} className="text-sm underline">← Livro</Link>
      <h1 className="mt-4 text-4xl font-semibold">Entidades e relações</h1>
      <p className="mt-3 max-w-2xl text-[var(--ink-soft)]">O resumo inicial deve ser seguro no primeiro ponto de conhecimento. Informações posteriores devem ser fatos separados.</p>
      {error && <p role="alert" className="mt-5 rounded-lg bg-red-50 p-3 text-red-800">{error}</p>}
      {message && <p role="status" className="mt-5 rounded-lg bg-green-50 p-3 text-green-800">{message}</p>}
      {units.length === 0 && <p className="mt-6 rounded-xl bg-amber-50 p-4">Crie unidades de leitura antes de cadastrar lore.</p>}
      <div className="mt-8 grid gap-6 lg:grid-cols-2">
        <section>
          <h2 className="text-2xl font-semibold">Conteúdo do livro</h2>
          <div className="mt-4 grid gap-2">
            {entities.map((entity) => (
              <Link key={entity.id} href={`/admin/entities/${entity.id}`}
                className="rounded-xl border bg-[var(--surface)] p-4 hover:border-[var(--olive)]">
                <strong className="block">{entity.name}</strong>
                <span className="text-xs text-[var(--ink-soft)]">{entity.type} · {units.find((unit) => unit.id === entity.firstKnownAtUnitId)?.safeLabel}</span>
              </Link>
            ))}
            {entities.length === 0 && <p>Nenhuma entidade criada.</p>}
          </div>
        </section>
        <form onSubmit={createEntity} className="grid content-start gap-4 rounded-2xl border bg-[var(--surface)] p-5">
          <h2 className="text-xl font-semibold">Nova entidade</h2>
          <label className="grid gap-1 text-sm">Nome
            <input required value={name} onChange={(event) => {
              setName(event.target.value);
              setSlug(event.target.value.toLowerCase().normalize("NFD").replace(/[\u0300-\u036f]/g, "").replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, ""));
            }} className="rounded-lg border px-3 py-2" />
          </label>
          <label className="grid gap-1 text-sm">Slug
            <input required value={slug} onChange={(event) => setSlug(event.target.value)} className="rounded-lg border px-3 py-2" />
          </label>
          <label className="grid gap-1 text-sm">Tipo
            <select value={type} onChange={(event) => setType(event.target.value)} className="rounded-lg border px-3 py-2">
              {["Character", "Faction", "Location", "Creature", "Item", "Event", "Organization", "Concept", "Other"].map((option) => <option key={option}>{option}</option>)}
            </select>
          </label>
          <label className="grid gap-1 text-sm">Resumo inicial sem spoilers
            <textarea rows={3} value={summary} onChange={(event) => setSummary(event.target.value)} className="rounded-lg border px-3 py-2" />
          </label>
          <label className="grid gap-1 text-sm">Primeira conhecida em
            <select required value={firstUnit} onChange={(event) => setFirstUnit(event.target.value)} className="rounded-lg border px-3 py-2">
              {units.map((unit) => <option key={unit.id} value={unit.id}>{unit.safeLabel} — {unit.title}</option>)}
            </select>
          </label>
          <button disabled={!firstUnit} className="justify-self-start rounded-lg bg-[var(--action-surface)] px-4 py-2 text-[var(--action-text)]">Criar entidade</button>
        </form>
      </div>
      <section className="mt-10 grid gap-6 lg:grid-cols-2">
        <div>
          <h2 className="text-2xl font-semibold">Relações</h2>
          <div className="mt-4 grid gap-2">
            {relations.map((relation) => (
              <div key={relation.id} className="flex items-center gap-3 rounded-xl border bg-[var(--surface)] p-4">
                <span className="flex-1">{names.get(relation.sourceEntityId)} → {names.get(relation.targetEntityId)} <small className="text-[var(--ink-soft)]">({relation.label || relation.relationType})</small></span>
                <button onClick={() => removeRelation(relation.id)} className="text-sm text-red-700">Excluir</button>
              </div>
            ))}
            {relations.length === 0 && <p>Nenhuma relação criada.</p>}
          </div>
        </div>
        <form onSubmit={createRelation} className="grid content-start gap-4 rounded-2xl border bg-[var(--surface)] p-5">
          <h2 className="text-xl font-semibold">Nova relação</h2>
          <label className="grid gap-1 text-sm">Origem
            <select required value={sourceId} onChange={(event) => setSourceId(event.target.value)} className="rounded-lg border px-3 py-2">
              {entities.map((entity) => <option key={entity.id} value={entity.id}>{entity.name}</option>)}
            </select>
          </label>
          <label className="grid gap-1 text-sm">Destino
            <select required value={targetId} onChange={(event) => setTargetId(event.target.value)} className="rounded-lg border px-3 py-2">
              {entities.map((entity) => <option key={entity.id} value={entity.id}>{entity.name}</option>)}
            </select>
          </label>
          <label className="grid gap-1 text-sm">Tipo
            <input required value={relationType} onChange={(event) => setRelationType(event.target.value)} className="rounded-lg border px-3 py-2" />
          </label>
          <label className="grid gap-1 text-sm">Rótulo opcional
            <input value={relationLabel} onChange={(event) => setRelationLabel(event.target.value)} className="rounded-lg border px-3 py-2" />
          </label>
          <label className="grid gap-1 text-sm">Revelada em
            <select required value={revealUnit} onChange={(event) => setRevealUnit(event.target.value)} className="rounded-lg border px-3 py-2">
              {units.map((unit) => <option key={unit.id} value={unit.id}>{unit.safeLabel}</option>)}
            </select>
          </label>
          <button disabled={!sourceId || !targetId || !revealUnit} className="justify-self-start rounded-lg bg-[var(--action-surface)] px-4 py-2 text-[var(--action-text)]">Criar relação</button>
        </form>
      </section>
    </div>
  );
}
