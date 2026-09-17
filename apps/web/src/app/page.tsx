import Link from "next/link";
import { ThemeToggle } from "@/components/theme-toggle";

const steps = [
  { number: "01", title: "Escolha seu livro", description: "Comece por uma história da sua biblioteca ou encontre uma nova leitura." },
  { number: "02", title: "Marque onde parou", description: "Capítulo por capítulo. Sua página fica como referência pessoal." },
  { number: "03", title: "Lembre e continue", description: "Encontre rapidamente o contexto que você já conheceu." },
];

export default function Home() {
  return (
    <div className="landing">
      <header className="landing-nav page-shell">
        <Link className="wordmark" href="/" aria-label="Mnemora, início"><span className="brand-mark" aria-hidden="true">m</span>Mnemora</Link>
        <nav aria-label="Navegação principal" className="landing-nav-actions">
          <a href="#como-funciona" className="nav-text-link">Como funciona</a>
          <Link href="/login" className="nav-text-link">Entrar</Link>
          <ThemeToggle compact />
          <Link href="/register" className="button button-small button-ink">Começar</Link>
        </nav>
      </header>
      <main id="main-content">
        <section className="hero page-shell">
          <div className="hero-copy">
            <div className="eyebrow"><span className="eyebrow-line" /> SEU COMPANHEIRO DE LEITURA</div>
            <h1>Quem era esse<br /><em>personagem</em> mesmo?</h1>
            <p className="hero-lead">Continue sua leitura sem voltar capítulos e sem tomar spoilers. Mnemora ajuda você a lembrar de quem é quem, exatamente até onde você leu.</p>
            <div className="hero-actions">
              <Link className="button button-ink" href="/register">Começar gratuitamente <span aria-hidden="true">↗</span></Link>
              <a className="button button-outline" href="#como-funciona">Ver como funciona</a>
            </div>
            <p className="hero-note">Remember the story. Not the spoilers.</p>
          </div>
          <div className="hero-visual" role="img" aria-label="Exemplo de uma busca que mostra somente lembranças já reveladas pela leitura">
            <div aria-hidden="true" className="hero-visual-content">
            <div className="visual-spine visual-spine-one" /><div className="visual-spine visual-spine-two" />
            <div className="demo-window">
              <div className="demo-toolbar"><span className="demo-dots"><i /><i /><i /></span><span>NO MEIO DA LEITURA</span><span>⌘ K</span></div>
              <div className="demo-content">
                <span className="demo-kicker">RECALL · O MAPA DAS MARÉS</span>
                <h2>Quem você está tentando lembrar?</h2>
                <div className="demo-search"><span aria-hidden="true">⌕</span> aquela cartógrafa da ilha… <span className="demo-caret" /></div>
                <div className="demo-result">
                  <div className="demo-avatar">A</div>
                  <div><span className="demo-result-label">VOCÊ PROVAVELMENTE ESTÁ PENSANDO EM</span><strong>Amira Vale</strong><p>A cartógrafa que guardava a única rota segura até o Farol de Vidro.</p></div>
                  <span className="demo-arrow" aria-hidden="true">↗</span>
                </div>
                <div className="demo-safe"><span aria-hidden="true">✦</span> Só o que você já descobriu</div>
              </div>
            </div>
            <span className="visual-annotation">uma lembrança,<br />não um resumo.</span>
            </div>
          </div>
        </section>
        <section className="promise-band"><div className="page-shell promise-inner"><span>LEIA NO SEU RITMO</span><span className="promise-divider" /><p>O mundo da história cresce com a sua leitura.</p><span className="promise-star" aria-hidden="true">✳</span></div></section>
        <section id="como-funciona" className="section page-shell how-section">
          <div className="section-intro"><span className="section-index">01 / COMO FUNCIONA</span><h2>Um pequeno espaço<br />para <em>lembrar.</em></h2><p>Abra o app, reconheça a pessoa ou lugar em segundos e volte à história. Sem artigos intermináveis.</p></div>
          <div className="step-grid">{steps.map((step) => <article className="step-card" key={step.number}><span>{step.number}</span><h3>{step.title}</h3><p>{step.description}</p></article>)}</div>
        </section>
        <section className="section safe-section"><div className="page-shell safe-inner"><div><span className="section-index">02 / SEM SPOILERS</span><h2>O futuro da história<br /><em>fica no futuro.</em></h2><p>Cada informação tem um ponto de revelação. Mnemora conhece seu progresso e mostra apenas o que sua leitura já revelou.</p><Link href="/register" className="text-action">Comece no seu ritmo <span aria-hidden="true">↗</span></Link></div><div className="safe-illustration"><div className="reading-line"><span>CAPÍTULO 01</span><span>CAPÍTULO 02</span><span>CAPÍTULO 03</span><span>···</span></div><div className="reading-track"><i /><i /><i className="current" /><i className="future" /></div><div className="safe-card"><span>VOCÊ ESTÁ AQUI</span><strong>O que já foi lido está ao alcance.</strong><p>O restante espera por você.</p></div></div></div></section>
        <section className="section page-shell feature-section"><span className="section-index">03 / DENTRO DO LIVRO</span><div className="feature-heading"><h2>Histórias complexas.<br /><em>Lembranças simples.</em></h2><p>Personagens, lugares, relações e acontecimentos organizados como pistas curtas para sua memória.</p></div><div className="feature-grid"><div className="feature-card"><span className="feature-icon" aria-hidden="true">⌕</span><h3>Recall imediato</h3><p>O nome pode escapar. A sensação de reconhecer, não.</p></div><div className="feature-card"><span className="feature-icon" aria-hidden="true">◫</span><h3>Contexto que acompanha</h3><p>Acompanhe o que você já sabe sobre o universo do livro.</p></div><div className="feature-card"><span className="feature-icon" aria-hidden="true">◇</span><h3>Sua leitura, sua medida</h3><p>Atualize o capítulo atual e mantenha a experiência segura.</p></div></div></section>
        <section className="closing-cta"><div className="page-shell"><span className="section-index">VOLTAR PARA A HISTÓRIA</span><h2>Não perca o fio.<br /><em>Continue lendo.</em></h2><Link className="button button-light" href="/register">Criar minha conta <span aria-hidden="true">↗</span></Link></div></section>
      </main>
      <footer className="landing-footer page-shell"><Link href="/" className="wordmark"><span className="brand-mark" aria-hidden="true">m</span>Mnemora</Link><span>Remember the story. Not the spoilers.</span><span>© {new Date().getFullYear()} Mnemora</span></footer>
    </div>
  );
}
