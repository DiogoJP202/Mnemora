import Link from "next/link";
export default function ForgotPasswordPage() {
  return <main className="simple-page page-shell"><Link href="/" className="wordmark"><span className="brand-mark" aria-hidden="true">m</span>Mnemora</Link><div className="simple-card"><span className="section-index">ACESSO À CONTA</span><h1>Recuperação de senha</h1><p>A recuperação automática de senha estará disponível em uma próxima versão. Se você não consegue entrar, fale com o administrador desta instalação.</p><Link href="/login" className="button button-ink">Voltar ao login</Link></div></main>;
}
