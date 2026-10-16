"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { apiGet, apiWrite } from "@/lib/api";
import { BookCover } from "@/components/reader/book-cover";
import { BookLoreNav } from "@/components/reader/book-lore-nav";
import { MemoryPackUnavailable } from "@/components/reader/memory-pack-unavailable";
import { detailMetadata } from "@/components/reader/book-metadata";
import type { Book, LibraryBook, ReadingUnit } from "@/lib/reader-types";

const statusLabels: Record<LibraryBook["status"], string> = {
  WantToRead: "Quero ler",
  Reading: "Lendo",
  Paused: "Pausado",
  Finished: "Concluído",
};

const statusOptions = Object.entries(statusLabels) as Array<
  [LibraryBook["status"], string]
>;

export function BookDetail({ bookId }: { bookId: string }) {
  const router = useRouter();
  const removeButtonRef = useRef<HTMLButtonElement>(null);
  const removalHeadingRef = useRef<HTMLHeadingElement>(null);
  const [book, setBook] = useState<Book | null>(null);
  const [libraryItem, setLibraryItem] = useState<LibraryBook | null>(null);
  const [units, setUnits] = useState<ReadingUnit[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [removing, setRemoving] = useState(false);
  const [confirmRemoval, setConfirmRemoval] = useState(false);
  const [unitId, setUnitId] = useState("");
  const [page, setPage] = useState("");
  const [status, setStatus] = useState<LibraryBook["status"]>("WantToRead");
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    let active = true;

    async function load() {
      try {
        const [foundBook, library] = await Promise.all([
          apiGet<Book>(`/api/books/${bookId}`),
          apiGet<LibraryBook[]>("/api/library"),
        ]);
        if (!active) return;

        const foundItem = library.find((item) => item.book.id === bookId) ?? null;
        setBook(foundBook);
        setLibraryItem(foundItem);
        setUnitId(foundItem?.currentReadingUnitId ?? "");
        setPage(foundItem?.currentPage?.toString() ?? "");
        setStatus(foundItem?.status ?? "WantToRead");

        if (foundItem && foundBook.readingOutlineAvailable) {
          const readingUnits = await apiGet<ReadingUnit[]>(
            `/api/books/${bookId}/reading-units`,
          );
          if (active) setUnits(readingUnits);
        }
      } catch (cause) {
        if (active) {
          setError(
            cause instanceof Error
              ? cause.message
              : "Não foi possível carregar o livro.",
          );
        }
      } finally {
        if (active) setLoading(false);
      }
    }

    void load();
    return () => {
      active = false;
    };
  }, [bookId]);

  useEffect(() => {
    if (confirmRemoval) removalHeadingRef.current?.focus();
  }, [confirmRemoval]);

  async function addToLibrary() {
    setSaving(true);
    setError(null);
    setMessage(null);
    try {
      const item = await apiWrite<LibraryBook>(`/api/library/${bookId}`, "POST");
      setLibraryItem(item);
      setStatus(item.status);
      setMessage("Livro adicionado à sua biblioteca.");

      if (item.book.readingOutlineAvailable) {
        try {
          setUnits(
            await apiGet<ReadingUnit[]>(`/api/books/${bookId}/reading-units`),
          );
        } catch {
          setError(
            "O livro foi adicionado, mas as unidades de leitura não puderam ser carregadas agora.",
          );
        }
      }
    } catch (cause) {
      setError(
        cause instanceof Error
          ? cause.message
          : "Não foi possível adicionar o livro.",
      );
    } finally {
      setSaving(false);
    }
  }

  async function saveProgress(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const currentPage = page.trim() ? Number(page) : null;
    if (currentPage !== null && (!Number.isInteger(currentPage) || currentPage < 1)) {
      setError("Informe uma página válida ou deixe o campo vazio.");
      return;
    }

    setSaving(true);
    setError(null);
    setMessage(null);
    try {
      const updated = await apiWrite<LibraryBook>(
        `/api/library/${bookId}/progress`,
        "PATCH",
        {
          ...(book?.readingOutlineAvailable
            ? { currentReadingUnitId: unitId || null }
            : {}),
          currentPage,
          status,
        },
      );

      setLibraryItem(updated);
      setUnitId(updated.currentReadingUnitId ?? "");
      setPage(updated.currentPage?.toString() ?? "");
      setStatus(updated.status);
      setMessage(
        book?.readingOutlineAvailable
          ? "Seu ponto de leitura e status foram atualizados."
          : "O acompanhamento deste livro foi atualizado.",
      );

      if (book?.readingOutlineAvailable) {
        setUnits([]);
        try {
          setUnits(
            await apiGet<ReadingUnit[]>(`/api/books/${bookId}/reading-units`),
          );
        } catch {
          setError(
            "As alterações foram salvas, mas as unidades de leitura não puderam ser atualizadas agora.",
          );
        }
      }
    } catch (cause) {
      setError(
        cause instanceof Error
          ? cause.message
          : "Não foi possível salvar o progresso.",
      );
    } finally {
      setSaving(false);
    }
  }

  function cancelRemoval() {
    setConfirmRemoval(false);
    requestAnimationFrame(() => removeButtonRef.current?.focus());
  }

  async function removeFromLibrary() {
    setRemoving(true);
    setError(null);
    setMessage(null);
    try {
      await apiWrite(`/api/library/${bookId}`, "DELETE");
      router.replace("/app/library");
    } catch (cause) {
      setError(
        cause instanceof Error
          ? cause.message
          : "Não foi possível remover o livro da estante.",
      );
      setRemoving(false);
    }
  }

  if (loading) {
    return (
      <div className="reader-page" role="status">
        Abrindo o livro…
      </div>
    );
  }

  if (!book) {
    return (
      <div className="reader-page">
        <div className="form-alert" role="alert">
          {error ?? "Livro não encontrado."}
        </div>
        <Link href="/app/library" className="text-action">
          ← Voltar à biblioteca
        </Link>
      </div>
    );
  }

  const progress = Math.max(0, Math.min(100, libraryItem?.progressPercentage ?? 0));
  const packAvailable = book.memoryPackAvailable;
  const outlineAvailable = book.readingOutlineAvailable;
  const metadata = detailMetadata(book);
  const packLabel = book.memoryPackStatus === "Available"
    ? "MEMÓRIA DISPONÍVEL"
    : book.memoryPackStatus === "Preparing"
      ? outlineAvailable ? "ÍNDICE DISPONÍVEL" : "EM PREPARAÇÃO"
      : book.memoryPackStatus === "Requested"
        ? "PACOTE SOLICITADO"
        : "SOMENTE ESTANTE";

  return (
    <div className="reader-page book-detail-page">
      <Link href="/app/library" className="back-link">
        ← Minha biblioteca
      </Link>

      <div className="book-hero">
        <BookCover title={book.title} coverUrl={book.coverUrl} large />
        <div className="book-hero-copy">
          <span
            className={`book-pack-badge${packAvailable ? " book-pack-badge-ready" : ""}`}
          >
            {packLabel}
          </span>
          <h1>{book.title}</h1>
          {book.subtitle && <p className="book-subtitle">{book.subtitle}</p>}
          <p className="book-author">por {book.author}</p>

          {metadata.length > 0 && (
            <dl className="book-edition-metadata" aria-label="Dados desta edição">
              {metadata.map((row) => (
                <div key={row.label}>
                  <dt>{row.label}</dt>
                  <dd>{row.value}</dd>
                </div>
              ))}
            </dl>
          )}

          {libraryItem ? (
            outlineAvailable ? (
              <div className="current-progress">
                <span>SEU PROGRESSO</span>
                <strong>
                  {libraryItem.currentReadingUnitId
                    ? `${Math.round(progress)}%`
                    : "Ainda não começou"}
                </strong>
                <div
                  className="book-progress-track"
                  role="progressbar"
                  aria-label={`Progresso em ${book.title}`}
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-valuenow={Math.round(progress)}
                >
                  <i aria-hidden="true" style={{ width: `${progress}%` }} />
                </div>
              </div>
            ) : (
              <div className="current-progress shelf-progress">
                <span>ACOMPANHAMENTO</span>
                <strong>{statusLabels[libraryItem.status]}</strong>
                {libraryItem.currentPage && <small>Página {libraryItem.currentPage}</small>}
              </div>
            )
          ) : (
            <button
              className="button button-ink"
              onClick={addToLibrary}
              disabled={saving}
              type="button"
            >
              {saving ? "Adicionando…" : "Adicionar à minha biblioteca"}
              <span aria-hidden="true">＋</span>
            </button>
          )}
        </div>
      </div>

      {error && (
        <div className="form-alert" role="alert">
          {error}
        </div>
      )}
      {message && (
        <div className="form-success" role="status">
          {message}
        </div>
      )}

      {libraryItem && (
        <BookLoreNav bookId={bookId} active="" memoryPackAvailable={packAvailable} />
      )}

      {libraryItem && !packAvailable && (
        <MemoryPackUnavailable
          bookId={bookId}
          initialStatus={book.memoryPackStatus}
          canRequest={book.canRequestMemoryPack}
          outlineAvailable={outlineAvailable}
          onRequested={() => setBook({
            ...book,
            memoryPackStatus: "Requested",
          })}
        />
      )}

      {packAvailable && (book.memoryPackAttribution || book.memoryPackSourceUrl) && (
        <section
          aria-labelledby="memory-pack-credits-title"
          className="my-6 min-w-0 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5"
        >
          <span className="section-index">CRÉDITOS</span>
          <h2 id="memory-pack-credits-title" className="mt-2 text-xl font-semibold">Fontes deste pacote</h2>
          {book.memoryPackAttribution && (
            <p className="mt-3 whitespace-pre-wrap break-words text-sm text-[var(--ink-soft)]">
              {book.memoryPackAttribution}
            </p>
          )}
          {book.memoryPackAttribution?.includes("CC BY-SA 4.0") && (
            <a href="https://creativecommons.org/licenses/by-sa/4.0/" target="_blank" rel="noopener noreferrer" className="mt-2 inline-flex min-h-11 items-center text-sm underline">
              Consultar licença CC BY-SA 4.0
            </a>
          )}
          {book.memoryPackSourceUrl && (
            <div className="mt-2">
              <p id="memory-pack-source-warning" className="text-sm text-[var(--ink-soft)]">
                A fonte externa pode contar a história inteira e conter spoilers além do seu ponto de leitura.
              </p>
              <a href={book.memoryPackSourceUrl} target="_blank" rel="noopener noreferrer" aria-describedby="memory-pack-source-warning" className="mt-2 inline-flex min-h-11 items-center text-sm underline">
                Abrir fonte original em outra aba
              </a>
            </div>
          )}
        </section>
      )}

      {libraryItem && (
        <>
          <div className="reading-layout">
            <section className="reading-card">
              <div className="reading-card-head">
                <span className="section-index">
                  {outlineAvailable ? "PONTO DE LEITURA" : "SOMENTE ESTANTE"}
                </span>
                <h2>
                  {outlineAvailable ? (
                    <>
                      Onde você <em>parou?</em>
                    </>
                  ) : (
                    <>
                      Acompanhe <em>sua leitura.</em>
                    </>
                  )}
                </h2>
                <p>
                  {outlineAvailable
                    ? packAvailable
                      ? "Seu ponto determina o que Mnemora pode mostrar sobre a história. Você pode avançar, voltar ou limpar esse ponto quando quiser."
                      : "Use as unidades publicadas para guardar seu ponto. O conhecimento da história continua fechado enquanto o pacote é revisado."
                    : "Este livro ainda não tem personagens e acontecimentos preparados pelo Mnemora. Você pode guardar o status, a página e suas notas."}
                </p>
              </div>

              {outlineAvailable && units.length === 0 && (
                <div className="pack-empty">
                  <span aria-hidden="true">◫</span>
                  <h3>As unidades não estão disponíveis agora.</h3>
                  <p>Você ainda pode atualizar o status e a página desta leitura.</p>
                </div>
              )}

              <form onSubmit={saveProgress} className="progress-form">
                <div className="field">
                  <label htmlFor="reading-status">Status da leitura</label>
                  <select
                    id="reading-status"
                    value={status}
                    disabled={saving || removing || confirmRemoval}
                    onChange={(event) =>
                      setStatus(event.target.value as LibraryBook["status"])
                    }
                  >
                    {statusOptions.map(([value, label]) => (
                      <option value={value} key={value}>
                        {label}
                      </option>
                    ))}
                  </select>
                </div>

                {outlineAvailable && units.length > 0 && (
                  <div className="field">
                    <label htmlFor="reading-unit">
                      Unidade de leitura <span>(opcional)</span>
                    </label>
                    <select
                      id="reading-unit"
                      value={unitId}
                      disabled={saving || removing || confirmRemoval}
                      onChange={(event) => setUnitId(event.target.value)}
                      aria-describedby="reading-unit-help"
                    >
                      <option value="">Nenhuma — limpar ponto de leitura</option>
                      {[...units]
                        .sort((a, b) => a.orderIndex - b.orderIndex)
                        .map((unit) => (
                          <option value={unit.id} key={unit.id}>
                            {unit.title}
                          </option>
                        ))}
                    </select>
                    <span id="reading-unit-help" className="field-help">
                      Ao limpar o ponto, o conteúdo da história volta a ficar protegido.
                      Unidades futuras usam rótulos neutros.
                    </span>
                  </div>
                )}

                <div className="field">
                  <label htmlFor="current-page">
                    Página atual <span>(opcional)</span>
                  </label>
                  <input
                    id="current-page"
                    type="number"
                    inputMode="numeric"
                    min="1"
                    step="1"
                    value={page}
                    disabled={saving || removing || confirmRemoval}
                    onChange={(event) => setPage(event.target.value)}
                    placeholder="Ex.: 128"
                    aria-describedby="current-page-help"
                  />
                  <span id="current-page-help" className="field-help">
                    {outlineAvailable
                      ? "A página é uma referência pessoal; spoilers seguem a unidade de leitura."
                      : "Use a página como referência pessoal. Ela não libera conteúdo de memória."}
                  </span>
                </div>

                <button
                  className="button button-ink"
                  type="submit"
                  disabled={saving || removing || confirmRemoval}
                >
                  {saving ? "Salvando…" : "Salvar acompanhamento"}
                  <span aria-hidden="true">↗</span>
                </button>
              </form>
            </section>

            <aside className="reading-aside">
              <span className="section-index">
                 {outlineAvailable ? "LER SEM PRESSA" : "SEU ESPAÇO"}
              </span>
              <p>
                {outlineAvailable
                  ? packAvailable
                    ? "O que vem depois continua guardado. Mnemora mostra o que sua história já contou."
                    : "Os títulos futuros ficam neutros, e o conhecimento só será aberto depois da revisão editorial."
                  : "Mesmo sem pacote de memória, suas notas continuam privadas e ligadas a este livro."}
              </p>
              <span aria-hidden="true">{outlineAvailable ? "✳" : "✎"}</span>
            </aside>
          </div>

          <section className="shelf-management" aria-labelledby="shelf-management-title">
            <div>
              <span className="section-index">GESTÃO DA ESTANTE</span>
              <h2 id="shelf-management-title">Remover este livro</h2>
              <p>
                Use esta opção quando não quiser mais acompanhar esta leitura no Mnemora.
              </p>
            </div>

            {!confirmRemoval ? (
              <button
                ref={removeButtonRef}
                className="button button-outline shelf-remove-button"
                type="button"
                disabled={saving}
                onClick={() => {
                  setError(null);
                  setMessage(null);
                  setConfirmRemoval(true);
                }}
              >
                Remover da estante
              </button>
            ) : (
              <div
                className="shelf-removal-confirmation"
                role="alertdialog"
                aria-labelledby="removal-confirmation-title"
                aria-describedby="removal-confirmation-description"
                onKeyDown={(event) => {
                  if (event.key === "Escape" && !removing) {
                    event.preventDefault();
                    cancelRemoval();
                  }
                }}
              >
                <h3
                  id="removal-confirmation-title"
                  ref={removalHeadingRef}
                  tabIndex={-1}
                >
                  Remover “{book.title}” da estante?
                </h3>
                <p id="removal-confirmation-description">
                  O progresso, as notas privadas e o histórico deste livro serão apagados.
                  Esta ação não pode ser desfeita.
                </p>
                <div className="shelf-removal-actions">
                  <button
                    className="button button-danger"
                    type="button"
                    disabled={removing || saving}
                    onClick={removeFromLibrary}
                  >
                    {removing ? "Removendo…" : "Sim, remover livro"}
                  </button>
                  <button
                    className="button button-outline"
                    type="button"
                    disabled={removing}
                    onClick={cancelRemoval}
                  >
                    Cancelar
                  </button>
                </div>
              </div>
            )}
          </section>
        </>
      )}
    </div>
  );
}
