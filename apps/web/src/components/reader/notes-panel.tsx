"use client";

import { useCallback, useEffect, useState } from "react";
import { apiGet, apiWrite } from "@/lib/api";
import type { UserNote } from "@/lib/memory-types";

export function NotesPanel({
  bookId,
  entityId,
  compact = false,
}: {
  bookId: string;
  entityId?: string;
  compact?: boolean;
}) {
  const [notes, setNotes] = useState<UserNote[] | null>(null);
  const [content, setContent] = useState("");
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingContent, setEditingContent] = useState("");
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState("");

  const load = useCallback(async () => {
    const suffix = entityId ? `?entityId=${encodeURIComponent(entityId)}` : "";
    setNotes(await apiGet<UserNote[]>(`/api/books/${bookId}/notes${suffix}`));
  }, [bookId, entityId]);

  useEffect(() => {
    let active = true;
    const suffix = entityId ? `?entityId=${encodeURIComponent(entityId)}` : "";
    apiGet<UserNote[]>(`/api/books/${bookId}/notes${suffix}`)
      .then((found) => { if (active) setNotes(found); })
      .catch((cause) => {
        if (active) setError(cause instanceof Error ? cause.message : "Não foi possível carregar suas notas.");
      });
    return () => { active = false; };
  }, [bookId, entityId]);

  async function createNote(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const cleaned = content.trim();
    if (!cleaned) return;
    setBusyId("new");
    setError(null);
    setMessage("");
    try {
      await apiWrite(`/api/books/${bookId}/notes`, "POST", {
        content: cleaned,
        ...(entityId ? { entityId } : {}),
      });
      setContent("");
      await load();
      setMessage("Nota salva somente para você.");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível salvar a nota.");
    } finally {
      setBusyId(null);
    }
  }

  async function updateNote(noteId: string) {
    const cleaned = editingContent.trim();
    if (!cleaned) return;
    setBusyId(noteId);
    setError(null);
    setMessage("");
    try {
      await apiWrite(`/api/notes/${noteId}`, "PUT", { content: cleaned });
      setEditingId(null);
      setEditingContent("");
      await load();
      setMessage("Nota atualizada.");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível atualizar a nota.");
    } finally {
      setBusyId(null);
    }
  }

  async function deleteNote(noteId: string) {
    if (!window.confirm("Excluir esta nota privada?")) return;
    setBusyId(noteId);
    setError(null);
    setMessage("");
    try {
      await apiWrite(`/api/notes/${noteId}`, "DELETE");
      await load();
      setMessage("Nota excluída.");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível excluir a nota.");
    } finally {
      setBusyId(null);
    }
  }

  function beginEditing(note: UserNote) {
    setEditingId(note.id);
    setEditingContent(note.content);
    setMessage("");
  }

  async function retryLoad() {
    setError(null);
    try {
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível carregar suas notas.");
    }
  }

  return (
    <section className={`notes-panel${compact ? " notes-panel-compact" : ""}`} aria-labelledby={entityId ? "entity-notes-title" : "book-notes-title"}>
      <div className="notes-panel-heading">
        <div>
          <span className="section-index">PRIVADO</span>
          <h2 id={entityId ? "entity-notes-title" : "book-notes-title"}>{entityId ? "Suas notas sobre esta lembrança" : "Seu caderno de leitura"}</h2>
        </div>
        <span aria-hidden="true">✎</span>
      </div>
      <p className="notes-privacy">Só você pode ler estas notas. Referências ligadas à história acompanham seu ponto de leitura.</p>
      <form className="note-form" onSubmit={createNote}>
        <label htmlFor={`note-content-${entityId ?? "book"}`}>Nova nota</label>
        <textarea
          id={`note-content-${entityId ?? "book"}`}
          value={content}
          onChange={(event) => setContent(event.target.value)}
          maxLength={4000}
          rows={compact ? 3 : 4}
          placeholder="Escreva algo que você quer lembrar…"
          disabled={busyId === "new"}
        />
        <div><small>{content.length}/4.000</small><button className="button button-ink button-small" type="submit" disabled={!content.trim() || busyId === "new"}>{busyId === "new" ? "Salvando…" : "Salvar nota"}</button></div>
      </form>
      <div className="notes-live" aria-live="polite">{message}</div>
      {error && <div className="form-alert" role="alert">{error}<button type="button" onClick={() => void retryLoad()}>Tentar novamente</button></div>}
      {notes === null && !error && <div className="note-list" role="status"><div className="note-card skeleton" /></div>}
      {notes?.length === 0 && <div className="knowledge-empty"><p>Você ainda não escreveu nenhuma nota aqui.</p></div>}
      {!!notes?.length && (
        <div className="note-list">
          {notes.map((note) => (
            <article className="note-card" key={note.id}>
              {editingId === note.id ? (
                <div className="note-edit">
                  <label className="sr-only" htmlFor={`edit-note-${note.id}`}>Editar nota</label>
                  <textarea id={`edit-note-${note.id}`} value={editingContent} maxLength={4000} rows={4} onChange={(event) => setEditingContent(event.target.value)} />
                  <div className="note-actions"><button type="button" onClick={() => setEditingId(null)}>Cancelar</button><button type="button" onClick={() => void updateNote(note.id)} disabled={!editingContent.trim() || busyId === note.id}>Salvar</button></div>
                </div>
              ) : (
                <>
                  <div className="note-meta"><span>{note.entityId ? "NOTA DE CONHECIMENTO" : note.readingUnitId ? "NOTA DE LEITURA" : "NOTA DO LIVRO"}</span><time dateTime={note.updatedAt}>{new Intl.DateTimeFormat("pt-BR", { dateStyle: "medium" }).format(new Date(note.updatedAt))}</time></div>
                  <p>{note.content}</p>
                  <div className="note-actions"><button type="button" onClick={() => beginEditing(note)}>Editar</button><button type="button" onClick={() => void deleteNote(note.id)} disabled={busyId === note.id}>Excluir</button></div>
                </>
              )}
            </article>
          ))}
        </div>
      )}
    </section>
  );
}
