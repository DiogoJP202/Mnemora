"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { apiGet, type AuthUser } from "@/lib/api";

export function AdminShell({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const [user, setUser] = useState<AuthUser | null | undefined>(undefined);

  useEffect(() => {
    apiGet<AuthUser>("/api/auth/me").then(setUser).catch(() => {
      setUser(null);
      router.replace("/login");
    });
  }, [router]);

  if (user === undefined) return <div className="page-shell py-20">Verificando acesso…</div>;
  if (user === null) return <div className="page-shell py-20">Entrando…</div>;
  if (!user.isAdmin) {
    return (
      <div className="page-shell py-20">
        <h1 className="text-3xl font-semibold">Acesso restrito</h1>
        <p className="mt-3">Esta área é reservada a administradores.</p>
        <Link href="/app" className="mt-6 inline-block underline">Voltar ao aplicativo</Link>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-[#f7f7f5] text-[#20241f]">
      <header className="border-b border-[#d9ddd6] bg-white">
        <div className="page-shell flex flex-wrap items-center justify-between gap-4 py-4">
          <Link href="/admin" className="text-xl font-semibold tracking-tight">Mnemora <span className="text-sm font-normal">Admin</span></Link>
          <nav aria-label="Administração" className="flex items-center gap-5 text-sm">
            <Link href="/admin/books">Livros</Link>
            <Link href="/app">Aplicativo</Link>
          </nav>
        </div>
      </header>
      <main className="page-shell py-8">{children}</main>
    </div>
  );
}
