import Link from "next/link";
import { ThemeToggle } from "@/components/theme-toggle";
export default function ForgotPasswordPage() {
  return <main id="main-content" className="simple-page page-shell"><div className="simple-header"><Link href="/" className="wordmark"><span className="brand-mark" aria-hidden="true">m</span>Mnemora</Link><ThemeToggle /></div><div className="simple-card"><span className="section-index">ACESSO À CONTA</span><h1>Recuperação de senha</h1><p>A recuperação automática de senha estará disponível em uma próxima versão. Se você não consegue entrar, fale com o administrador desta instalação.</p><Link href="/login" className="button button-ink">Voltar ao login</Link></div></main>;
}
