import Link from "next/link";

const sections = [
  { segment: "", label: "Visão geral", requiresPack: false },
  { segment: "/entities", label: "Conhecimento", requiresPack: true },
  { segment: "/characters", label: "Personagens", requiresPack: true },
  { segment: "/factions", label: "Facções", requiresPack: true },
  { segment: "/locations", label: "Lugares", requiresPack: true },
  { segment: "/timeline", label: "Linha do tempo", requiresPack: true },
  { segment: "/recall", label: "Recall", requiresPack: true },
  { segment: "/notes", label: "Notas", requiresPack: false },
  { segment: "/review", label: "Revisão", requiresPack: true },
];

export function BookLoreNav({ bookId, active, memoryPackAvailable = true }: { bookId: string; active: string; memoryPackAvailable?: boolean }) {
  return <nav className="book-lore-nav" aria-label="Explorar este livro">{sections.map((item) => {
    const href = `/app/books/${bookId}${item.segment}`;
    const preparing = item.requiresPack && !memoryPackAvailable;
    return <Link
      key={href}
      href={href}
      aria-current={active === item.segment ? "page" : undefined}
      className={preparing ? "book-lore-nav-preparing" : undefined}
      title={preparing ? "Disponível quando o pacote de memória for publicado" : undefined}
    >
      {item.label}
      {preparing && <span className="sr-only"> — em preparação</span>}
    </Link>;
  })}</nav>;
}
