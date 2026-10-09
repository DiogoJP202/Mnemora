"use client";

import Link from "next/link";
import { useState } from "react";
import { apiWrite } from "@/lib/api";
import type { MemoryPackStatus } from "@/lib/reader-types";

type Props = {
  bookId: string;
  initialStatus?: MemoryPackStatus;
  canRequest?: boolean;
  outlineAvailable?: boolean;
  onRequested?: () => void;
};

const copy: Record<Exclude<MemoryPackStatus, "Available">, {
  eyebrow: string;
  title: string;
  description: string;
}> = {
  Unavailable: {
    eyebrow: "PACOTE DE MEMÓRIA",
    title: "Ajude este livro a entrar na fila.",
    description: "Registre seu interesse. A equipe poderá preparar unidades, personagens, lugares, relações e acontecimentos sem transformar sinopses externas em spoilers.",
  },
  Requested: {
    eyebrow: "PEDIDO REGISTRADO",
    title: "Este livro já está na sua lista de pedidos.",
    description: "Seu pedido ajuda a priorizar a curadoria. Status, página e notas continuam disponíveis enquanto o pacote não chega.",
  },
  Preparing: {
    eyebrow: "EM PREPARAÇÃO",
    title: "O conteúdo está sendo revisado.",
    description: "As unidades podem chegar antes do conhecimento. Personagens, facções, lugares, timeline, Recall e revisão só serão liberados juntos após a validação antisspoiler.",
  },
};

export function MemoryPackUnavailable({
  bookId,
  initialStatus = "Unavailable",
  canRequest = false,
  outlineAvailable = false,
  onRequested,
}: Props) {
  const [requestedBookId, setRequestedBookId] = useState<string | null>(null);
  const status = initialStatus === "Available"
    ? "Preparing"
    : initialStatus === "Unavailable" && requestedBookId === bookId
      ? "Requested"
      : initialStatus;
  const [requesting, setRequesting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const content = status === "Unavailable" && !canRequest
    ? {
      eyebrow: "SEU ESPAÇO DE LEITURA",
      title: "Guarde suas próprias memórias deste livro.",
      description: "Você pode acompanhar o status, marcar a página e escrever notas privadas. Livros cadastrados manualmente ficam disponíveis somente para você.",
    }
    : copy[status];

  async function requestPack() {
    setRequesting(true);
    setError(null);
    try {
      await apiWrite(`/api/books/${bookId}/memory-pack-request`, "POST");
      setRequestedBookId(bookId);
      onRequested?.();
    } catch (cause) {
      setError(cause instanceof Error
        ? cause.message
        : "Não foi possível registrar o pedido agora.");
    } finally {
      setRequesting(false);
    }
  }

  return (
    <section className="memory-pack-state lore-empty" aria-labelledby="memory-pack-unavailable-title">
      <div className="memory-pack-state-copy">
        <span className="section-index">{content.eyebrow}</span>
        <h2 id="memory-pack-unavailable-title">{content.title}</h2>
        <p>{content.description}</p>
        {outlineAvailable && (
          <p className="memory-pack-outline-note">
            O índice de leitura já está disponível. Você pode marcar seu capítulo sem receber conhecimento ainda não publicado.
          </p>
        )}
        {error && <div className="form-alert" role="alert">{error}</div>}
        <div className="pack-unavailable-actions">
          {status === "Unavailable" && canRequest && (
            <button
              type="button"
              className="button button-ink"
              onClick={() => void requestPack()}
              disabled={requesting}
            >
              {requesting ? "Registrando…" : "Solicitar pacote de memória"}
              <span aria-hidden="true">↗</span>
            </button>
          )}
          <Link href={`/app/books/${bookId}/notes`} className="button button-outline">
            Abrir minhas notas
          </Link>
        </div>
      </div>
      <ul className="memory-pack-features" aria-label="Recursos do pacote">
        <li><span aria-hidden="true">01</span> Unidades e títulos futuros protegidos</li>
        <li><span aria-hidden="true">02</span> Personagens, facções e lugares</li>
        <li><span aria-hidden="true">03</span> Relações e linha do tempo</li>
        <li><span aria-hidden="true">04</span> Recall e revisão do que você já leu</li>
      </ul>
    </section>
  );
}
