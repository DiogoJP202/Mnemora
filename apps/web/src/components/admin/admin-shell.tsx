"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { apiGet, type AuthUser } from "@/lib/api";
import { ThemeToggle } from "@/components/theme-toggle";

export function AdminShell({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const [user, setUser] = useState<AuthUser | null | undefined>(undefined);

  useEffect(() => {
    apiGet<AuthUser>("/api/auth/me").then(setUser).catch(() => {
      setUser(null);
      router.replace("/login");
    });
  }, [router]);

  if (user === undefined) return <main id="main-content" className="page-shell py-20" role="status">Verificando acesso…</main>;
  if (user === null) return <main id="main-content" className="page-shell py-20" role="status">Entrando…</main>;
  if (!user.isAdmin) {
    return (
      <main id="main-content" className="page-shell py-20">
        <h1 className="text-3xl font-semibold">Acesso restrito</h1>
        <p className="mt-3">Esta área é reservada a administradores.</p>
        <Link href="/app" className="mt-6 inline-block underline">Voltar ao aplicativo</Link>
      </main>
    );
  }

  return (
    <div className="admin-shell min-h-screen">
      <header className="admin-header border-b">
        <div className="page-shell flex flex-wrap items-center justify-between gap-4 py-4">
          <Link href="/admin" className="text-xl font-semibold tracking-tight">Mnemora <span className="text-sm font-normal">Admin</span></Link>
          <nav aria-label="Administração" className="flex items-center gap-5 text-sm">
            <Link href="/admin/books">Livros</Link>
            <Link href="/app">Aplicativo</Link>
            <ThemeToggle compact />
          </nav>
        </div>
      </header>
      <main id="main-content" className="page-shell py-8">{children}</main>
    </div>
  );
}
