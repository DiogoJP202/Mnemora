import Link from "next/link";

const sections = [
  { segment: "", label: "Visão geral" },
  { segment: "/entities", label: "Conhecimento" },
  { segment: "/characters", label: "Personagens" },
  { segment: "/factions", label: "Facções" },
  { segment: "/locations", label: "Lugares" },
  { segment: "/timeline", label: "Linha do tempo" },
];

export function BookLoreNav({ bookId, active }: { bookId: string; active: string }) {
  return <nav className="book-lore-nav" aria-label="Explorar este livro">{sections.map((item) => {
    const href = `/app/books/${bookId}${item.segment}`;
    return <Link key={href} href={href} aria-current={active === item.segment ? "page" : undefined}>{item.label}</Link>;
  })}</nav>;
}
