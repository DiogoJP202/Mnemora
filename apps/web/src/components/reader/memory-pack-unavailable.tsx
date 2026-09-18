import Link from "next/link";

export function MemoryPackUnavailable({ bookId }: { bookId: string }) {
  return (
    <section className="empty-state compact lore-empty" aria-labelledby="memory-pack-unavailable-title">
      <div className="empty-symbol" aria-hidden="true">◫</div>
      <h2 id="memory-pack-unavailable-title">Memória ainda não disponível.</h2>
      <p>Este livro já pode ficar na sua estante com status, página e notas. Personagens, lugares e acontecimentos aparecerão quando o pacote de memória for preparado.</p>
      <div className="pack-unavailable-actions">
        <Link href={`/app/books/${bookId}`} className="button button-outline">Voltar ao livro</Link>
        <Link href={`/app/books/${bookId}/notes`} className="button button-ink">Abrir minhas notas <span aria-hidden="true">↗</span></Link>
      </div>
    </section>
  );
}
