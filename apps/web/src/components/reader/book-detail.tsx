"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { apiGet, apiWrite } from "@/lib/api";
import { BookCover } from "@/components/reader/book-cover";
import { BookLoreNav } from "@/components/reader/book-lore-nav";
import type { Book, LibraryBook, ReadingUnit } from "@/lib/reader-types";

const statusLabels: Record<LibraryBook["status"], string> = {
  WantToRead: "Quero ler",
  Reading: "Lendo",
  Paused: "Pausado",
  Finished: "Concluído",
};

export function BookDetail({ bookId }: { bookId: string }) {
  const [book, setBook] = useState<Book | null>(null);
  const [libraryItem, setLibraryItem] = useState<LibraryBook | null>(null);
  const [units, setUnits] = useState<ReadingUnit[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [unitId, setUnitId] = useState("");
  const [page, setPage] = useState("");
  const [status, setStatus] = useState<LibraryBook["status"]>("WantToRead");
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    Promise.all([apiGet<Book>(`/api/books/${bookId}`), apiGet<LibraryBook[]>("/api/library")])
      .then(async ([foundBook, library]) => {
        if (!active) return;
        const foundItem = library.find((item) => item.book.id === bookId) ?? null;
        setBook(foundBook);
        setLibraryItem(foundItem);
        setUnitId(foundItem?.currentReadingUnitId ?? "");
        setPage(foundItem?.currentPage?.toString() ?? "");
        setStatus(foundItem?.status ?? "WantToRead");
        if (foundItem && foundBook.memoryPackAvailable) {
          const readingUnits = await apiGet<ReadingUnit[]>(`/api/books/${bookId}/reading-units`);
          if (active) setUnits(readingUnits);
        }
      })
      .catch((cause) => { if (active) setError(cause instanceof Error ? cause.message : "Não foi possível carregar o livro."); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [bookId]);

  async function addToLibrary() {
    setSaving(true);
    setError(null);
    try {
      const item = await apiWrite<LibraryBook>(`/api/library/${bookId}`, "POST");
      setLibraryItem(item);
      setStatus(item.status);
      if (item.book.memoryPackAvailable) {
        setUnits(await apiGet<ReadingUnit[]>(`/api/books/${bookId}/reading-units`));
      }
      setMessage("Livro adicionado à sua biblioteca.");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível adicionar o livro.");
    } finally { setSaving(false); }
  }

  async function saveProgress(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (book?.memoryPackAvailable && !unitId) { setError("Escolha uma unidade de leitura."); return; }
    const currentPage = page.trim() ? Number(page) : null;
    if (currentPage !== null && (!Number.isInteger(currentPage) || currentPage < 1)) {
      setError("Informe uma página válida ou deixe o campo vazio.");
      return;
    }
    setSaving(true);
    setError(null);
    setMessage(null);
    try {
      let updated: LibraryBook;
      if (book?.memoryPackAvailable) {
        updated = await apiWrite<LibraryBook>(`/api/library/${bookId}/progress`, "PATCH", {
          currentReadingUnitId: unitId,
          currentPage,
        });
        setUnits(await apiGet<ReadingUnit[]>(`/api/books/${bookId}/reading-units`));
        setStatus(updated.status);
      } else {
        await apiWrite<LibraryBook>(`/api/library/${bookId}/progress`, "PATCH", { currentPage });
        updated = await apiWrite<LibraryBook>(`/api/library/${bookId}/status`, "PATCH", { status });
      }
      setLibraryItem(updated);
      setMessage(book?.memoryPackAvailable
        ? "Seu ponto de leitura foi atualizado."
        : "O acompanhamento deste livro foi atualizado.");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Não foi possível salvar o progresso.");
    } finally { setSaving(false); }
  }

  if (loading) return <div className="reader-page" role="status">Abrindo o livro…</div>;
  if (!book) return <div className="reader-page"><div className="form-alert" role="alert">{error ?? "Livro não encontrado."}</div><Link href="/app/library" className="text-action">← Voltar à biblioteca</Link></div>;

  const progress = Math.max(0, Math.min(100, libraryItem?.progressPercentage ?? 0));
  const packAvailable = book.memoryPackAvailable;

  return (
    <div className="reader-page book-detail-page">
      <Link href="/app/library" className="back-link">← Minha biblioteca</Link>
      <div className="book-hero"><BookCover title={book.title} coverUrl={book.coverUrl} large /><div><span className={`book-pack-badge${packAvailable ? " book-pack-badge-ready" : ""}`}>{packAvailable ? "PACOTE DE MEMÓRIA" : "SOMENTE ESTANTE"}</span><h1>{book.title}</h1><p className="book-author">por {book.author}</p>{libraryItem ? packAvailable ? <div className="current-progress"><span>SEU PROGRESSO</span><strong>{libraryItem.currentReadingUnitId ? `${Math.round(progress)}%` : "Ainda não começou"}</strong><div className="book-progress-track" role="progressbar" aria-label={`Progresso em ${book.title}`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(progress)}><i aria-hidden="true" style={{ width: `${progress}%` }} /></div></div> : <div className="current-progress shelf-progress"><span>ACOMPANHAMENTO</span><strong>{statusLabels[libraryItem.status]}</strong>{libraryItem.currentPage && <small>Página {libraryItem.currentPage}</small>}</div> : <button className="button button-ink" onClick={addToLibrary} disabled={saving} type="button">{saving ? "Adicionando…" : "Adicionar à minha biblioteca"} <span aria-hidden="true">＋</span></button>}</div></div>
      {error && <div className="form-alert" role="alert">{error}</div>}
      {message && <div className="form-success" role="status">{message}</div>}
      {libraryItem && <BookLoreNav bookId={bookId} active="" memoryPackAvailable={packAvailable} />}
      {libraryItem && <div className="reading-layout"><section className="reading-card">{packAvailable ? <><div className="reading-card-head"><span className="section-index">PONTO DE LEITURA</span><h2>Onde você <em>parou?</em></h2><p>Esse ponto determina o que Mnemora pode mostrar sobre a história. Você pode avançar ou voltar quando quiser.</p></div>{units.length ? <form onSubmit={saveProgress} className="progress-form"><div className="field"><label htmlFor="reading-unit">Unidade de leitura</label><select id="reading-unit" value={unitId} onChange={(event) => setUnitId(event.target.value)} required aria-describedby="reading-unit-help"><option value="">Selecione onde parou</option>{[...units].sort((a, b) => a.orderIndex - b.orderIndex).map((unit) => <option value={unit.id} key={unit.id}>{unit.label}</option>)}</select><span id="reading-unit-help" className="field-help">Unidades ainda não lidas usam rótulos neutros para proteger a história.</span></div><div className="field"><label htmlFor="current-page">Página da sua edição <span>(opcional)</span></label><input id="current-page" type="number" inputMode="numeric" min="1" step="1" value={page} onChange={(event) => setPage(event.target.value)} placeholder="Ex.: 128" aria-describedby="current-page-help" /><span id="current-page-help" className="field-help">A página é só para você; spoilers seguem a unidade de leitura.</span></div><button className="button button-ink" type="submit" disabled={saving}>{saving ? "Salvando…" : "Salvar meu progresso"} <span aria-hidden="true">↗</span></button></form> : <div className="pack-empty"><span aria-hidden="true">◫</span><h3>O pacote está sendo preparado.</h3><p>Este livro já está na sua estante. As unidades de leitura aparecerão assim que o pacote de memória estiver pronto.</p></div>}</> : <><div className="reading-card-head"><span className="section-index">SOMENTE ESTANTE</span><h2>Acompanhe <em>sua leitura.</em></h2><p>Este livro ainda não tem personagens e acontecimentos preparados pelo Mnemora. Você pode guardar o status, a página e suas notas.</p></div><form onSubmit={saveProgress} className="progress-form"><div className="field"><label htmlFor="reading-status">Status da leitura</label><select id="reading-status" value={status} onChange={(event) => setStatus(event.target.value as LibraryBook["status"])}><option value="WantToRead">Quero ler</option><option value="Reading">Lendo</option><option value="Paused">Pausado</option><option value="Finished">Concluído</option></select></div><div className="field"><label htmlFor="current-page">Página atual <span>(opcional)</span></label><input id="current-page" type="number" inputMode="numeric" min="1" step="1" value={page} onChange={(event) => setPage(event.target.value)} placeholder="Ex.: 128" aria-describedby="current-page-help" /><span id="current-page-help" className="field-help">Use a página como referência pessoal. Ela não libera conteúdo de memória.</span></div><button className="button button-ink" type="submit" disabled={saving}>{saving ? "Salvando…" : "Salvar acompanhamento"} <span aria-hidden="true">↗</span></button></form></>}</section><aside className="reading-aside">{packAvailable ? <><span className="section-index">LER SEM PRESSA</span><p>O que vem depois continua guardado. Mnemora mostra o que sua história já contou.</p><span aria-hidden="true">✳</span></> : <><span className="section-index">SEU ESPAÇO</span><p>Mesmo sem pacote de memória, suas notas continuam privadas e ligadas a este livro.</p><span aria-hidden="true">✎</span></>}</aside></div>}
    </div>
  );
}
