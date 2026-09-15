"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { apiWrite, type AuthUser } from "@/lib/api";

const loginSchema = z.object({
  email: z.email("Informe um e-mail válido."),
  password: z.string().min(1, "Informe sua senha."),
});
const registerSchema = loginSchema.extend({
  password: z.string().min(10, "Use ao menos 10 caracteres."),
});
type Credentials = z.infer<typeof loginSchema>;

export function AuthForm({ mode }: { mode: "login" | "register" }) {
  const router = useRouter();
  const [serverError, setServerError] = useState<string | null>(null);
  const schema = mode === "register" ? registerSchema : loginSchema;
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<Credentials>({
    resolver: zodResolver(schema),
  });

  async function submit(values: Credentials) {
    setServerError(null);
    try {
      await apiWrite<AuthUser>(`/api/auth/${mode}`, "POST", values);
      router.replace("/app/library");
      router.refresh();
    } catch (error) {
      setServerError(error instanceof Error ? error.message : "Não foi possível entrar.");
    }
  }

  return (
    <div className="auth-page">
      <div className="auth-aside">
        <Link href="/" className="wordmark"><span className="brand-mark" aria-hidden="true">m</span>Mnemora</Link>
        <div className="auth-aside-copy"><span className="section-index">UM LUGAR PARA LEMBRAR</span><p>Volte à história com a memória em dia.</p><span>Remember the story. Not the spoilers.</span></div>
        <div className="auth-aside-lines" aria-hidden="true"><i /><i /><i /></div>
      </div>
      <main className="auth-main">
        <div className="auth-inner">
          <Link href="/" className="auth-back">← Voltar ao início</Link>
          <span className="section-index">{mode === "login" ? "BEM-VINDO DE VOLTA" : "COMECE A LEMBRAR"}</span>
          <h1>{mode === "login" ? <>Seu lugar na<br /><em>história.</em></> : <>Uma nova forma<br />de <em>lembrar.</em></>}</h1>
          <p className="auth-intro">{mode === "login" ? "Entre para continuar exatamente de onde sua leitura parou." : "Crie sua conta para acompanhar o que você leu, no seu ritmo."}</p>
          <form onSubmit={handleSubmit(submit)} noValidate className="auth-form">
            <div className="field"><label htmlFor="email">E-mail</label><input id="email" type="email" autoComplete="email" placeholder="voce@exemplo.com" aria-invalid={!!errors.email} {...register("email")} />{errors.email && <span className="field-error">{errors.email.message}</span>}</div>
            <div className="field"><div className="field-label-row"><label htmlFor="password">Senha</label>{mode === "login" && <Link href="/forgot-password">Esqueceu?</Link>}</div><input id="password" type="password" autoComplete={mode === "login" ? "current-password" : "new-password"} placeholder={mode === "login" ? "Sua senha" : "Ao menos 10 caracteres"} aria-invalid={!!errors.password} {...register("password")} />{errors.password && <span className="field-error">{errors.password.message}</span>}</div>
            {serverError && <div className="form-alert" role="alert">{serverError}</div>}
            <button className="button button-ink auth-submit" disabled={isSubmitting} type="submit">{isSubmitting ? "Aguarde…" : mode === "login" ? "Entrar" : "Criar minha conta"} <span aria-hidden="true">↗</span></button>
          </form>
          <p className="auth-switch">{mode === "login" ? "Ainda não tem conta?" : "Já tem conta?"} <Link href={mode === "login" ? "/register" : "/login"}>{mode === "login" ? "Criar uma conta" : "Entrar"}</Link></p>
        </div>
      </main>
    </div>
  );
}
