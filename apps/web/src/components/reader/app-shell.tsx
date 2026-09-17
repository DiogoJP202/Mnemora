"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { apiGet, apiWrite, type AuthUser } from "@/lib/api";
import { ThemeToggle } from "@/components/theme-toggle";

export function AppShell({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const [user, setUser] = useState<AuthUser | null | undefined>(undefined);
  const [loggingOut, setLoggingOut] = useState(false);

  useEffect(() => {
    apiGet<AuthUser>("/api/auth/me").then(setUser).catch(() => {
      setUser(null);
      router.replace("/login");
    });
  }, [router]);

  async function logout() {
    setLoggingOut(true);
    try {
      await apiWrite("/api/auth/logout", "POST");
      router.replace("/");
      router.refresh();
    } catch {
      setLoggingOut(false);
    }
  }

  if (user === undefined) return <main id="main-content" className="app-loading" role="status"><span className="brand-mark" aria-hidden="true">m</span><p>Preparando sua biblioteca…</p></main>;
  if (user === null) return <main id="main-content" className="app-loading" role="status"><p>Entrando…</p></main>;

  const isLibrary = pathname === "/app/library" || pathname.startsWith("/app/books/");
  const isSearch = pathname === "/app/search";

  return (
    <div className="app-frame">
      <aside className="app-sidebar">
        <Link href="/app/library" className="wordmark"><span className="brand-mark" aria-hidden="true">m</span>Mnemora</Link>
        <p className="sidebar-label">SEU ESPAÇO DE LEITURA</p>
        <nav aria-label="Aplicativo" className="sidebar-links">
          <Link href="/app/library" aria-current={isLibrary ? "page" : undefined}><span aria-hidden="true">◫</span> Biblioteca</Link>
          <Link href="/app/search" aria-current={isSearch ? "page" : undefined}><span aria-hidden="true">⌕</span> Encontrar livros</Link>
          {user.isAdmin && <Link href="/admin"><span aria-hidden="true">⚙</span> Administração</Link>}
        </nav>
        <div className="sidebar-bottom"><span className="sidebar-quote">“Um livro é uma memória que você ainda não viveu.”</span><span className="sidebar-email" title={user.email}>{user.email}</span><button onClick={logout} disabled={loggingOut} type="button">Sair da conta <span aria-hidden="true">↗</span></button></div>
      </aside>
      <div className="app-content">
        <header className="app-topbar"><Link href="/app/library" className="wordmark"><span className="brand-mark" aria-hidden="true">m</span>Mnemora</Link><div className="app-topbar-actions"><span className="app-topbar-tag">Remember the story. Not the spoilers.</span><ThemeToggle compact /></div></header>
        <main id="main-content" className="app-main">{children}</main>
      </div>
      <nav className="mobile-nav" aria-label="Navegação do aplicativo">
        <Link href="/app/library" aria-current={isLibrary ? "page" : undefined}><span aria-hidden="true">◫</span>Biblioteca</Link>
        <Link href="/app/search" aria-current={isSearch ? "page" : undefined}><span aria-hidden="true">⌕</span>Encontrar</Link>
        {user.isAdmin && <Link href="/admin"><span aria-hidden="true">⚙</span>Admin</Link>}
        <button onClick={logout} disabled={loggingOut} type="button"><span aria-hidden="true">↗</span>{loggingOut ? "Saindo" : "Sair"}</button>
      </nav>
    </div>
  );
}
