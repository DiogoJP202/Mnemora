"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { apiGet, apiWrite, type AuthUser } from "@/lib/api";

export function AppShell({ children }: { children: React.ReactNode }) {
  const router = useRouter();
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

  if (user === undefined) return <div className="app-loading" role="status"><span className="brand-mark">m</span><p>Preparando sua biblioteca…</p></div>;
  if (user === null) return <div className="app-loading" role="status"><p>Entrando…</p></div>;

  return (
    <div className="app-frame">
      <aside className="app-sidebar">
        <Link href="/app/library" className="wordmark"><span className="brand-mark" aria-hidden="true">m</span>Mnemora</Link>
        <p className="sidebar-label">SEU ESPAÇO DE LEITURA</p>
        <nav aria-label="Aplicativo" className="sidebar-links">
          <Link href="/app/library"><span aria-hidden="true">◫</span> Biblioteca</Link>
          <Link href="/app/search"><span aria-hidden="true">⌕</span> Encontrar livros</Link>
          {user.isAdmin && <Link href="/admin"><span aria-hidden="true">⚙</span> Administração</Link>}
        </nav>
        <div className="sidebar-bottom"><span className="sidebar-quote">“Um livro é uma memória que você ainda não viveu.”</span><span className="sidebar-email" title={user.email}>{user.email}</span><button onClick={logout} disabled={loggingOut} type="button">Sair da conta <span aria-hidden="true">↗</span></button></div>
      </aside>
      <div className="app-content">
        <header className="app-topbar"><Link href="/app/library" className="wordmark"><span className="brand-mark" aria-hidden="true">m</span>Mnemora</Link><span className="app-topbar-tag">Remember the story. Not the spoilers.</span></header>
        <main className="app-main">{children}</main>
      </div>
      <nav className="mobile-nav" aria-label="Navegação do aplicativo"><Link href="/app/library"><span aria-hidden="true">◫</span>Biblioteca</Link><Link href="/app/search"><span aria-hidden="true">⌕</span>Encontrar</Link></nav>
    </div>
  );
}
