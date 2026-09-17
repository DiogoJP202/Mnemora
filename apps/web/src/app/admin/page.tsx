import Link from "next/link";

export default function AdminPage() {
  return (
    <div>
      <p className="text-sm uppercase tracking-[.16em] text-[var(--ink-soft)]">Conteúdo</p>
      <h1 className="mt-2 text-4xl font-semibold">Painel administrativo</h1>
      <p className="mt-4 max-w-xl text-[var(--ink-soft)]">Crie livros, organize unidades de leitura e defina quando cada informação pode aparecer.</p>
      <Link href="/admin/books" className="mt-8 inline-block rounded-xl bg-[var(--action-surface)] px-5 py-3 text-[var(--action-text)]">Gerenciar livros</Link>
    </div>
  );
}
