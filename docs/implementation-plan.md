# Plano de implementação do Mnemora

Mnemora é um companion de leitura mobile-first. A consulta deve recuperar apenas o que o leitor já encontrou, sem transmitir conteúdo futuro ao navegador. O nome do produto, namespaces, banco, PWA e documentação é Mnemora. A interface usa português; a tagline pública é “Remember the story. Not the spoilers.”

## Arquitetura

Monorepo com `apps/web` (Next.js App Router, React, TypeScript, Tailwind) e `src/Mnemora.Api`, `src/Mnemora.Application`, `src/Mnemora.Domain`, `src/Mnemora.Infrastructure` (ASP.NET Core, EF Core, Identity, SQLite). A API REST vive em `/api`. O Next.js encaminha `/api/*` ao backend para manter a origem do navegador única. Toda resposta privada recebe `Cache-Control: no-store`. Cookies de autenticação são HttpOnly, SameSite Lax e Secure em produção; operações de escrita requerem antiforgery.

O banco `mnemora.db` usa migrations EF e permanece fora do Git. As tabelas principais são Book, ReadingUnit, UserBook, LoreEntity, EntityAlias, LoreFact, LoreRelation, UserNote e UserRecallActivity. ReadingUnit tem hierarquia e ordem absoluta por livro; o progresso usa a unidade, nunca a página. Alias, fato e relação têm unidade de revelação. Eventos podem ter ordem cronológica distinta da ordem de leitura.

O backend obtém o progresso autenticado e aplica uma política central para selecionar entidades, aliases, fatos e relações conhecidos antes de montar DTOs. Sem unidade registrada, nenhum lore é conhecido. Retroceder o progresso revoga visibilidade. Busca textual considera somente campos já visíveis, inclusive pistas de memória. A timeline lista apenas eventos conhecidos. O leitor vê rótulos neutros para unidades futuras cujo título possa revelar a história.

## Fases e gates de Git

Cada fase termina com validações pertinentes, inspeção do diff e de segredos, um commit `phase NN: ...` e `git push origin main`. Uma falha de push deve ser resolvida antes da fase seguinte.

| Fase | Entrega | Gate mínimo |
| --- | --- | --- |
| 00 | Plano, arquitetura e ignore | Revisão dos documentos e Git |
| 01 | Scaffolding, configuração, Identity, auth, migration inicial | Build web/API e fluxo de auth |
| 02 | Domínio, integridade, migration | Build, migration e testes de invariantes |
| 03 | Política antisspoiler e projeções | Testes de fronteira e endpoints |
| 04 | Administração, reordenação, preview e seed original | CRUD, autorização e seed |
| 05 | Landing, cadastro, biblioteca, livro e progresso | Jornada do leitor em mobile/desktop |
| 06 | Personagens, detalhes, facções, lugares e timeline | Visibilidade em cada tela |
| 07 | Recall textual por campos liberados | Busca por termos/aliases futuros |
| 08 | Notas privadas e revisão simples | Isolamento por usuário |
| 09 | PWA, temas, acessibilidade e acabamento | Manifest, instalação e cache seguro |
| 10 | Revisão final e documentação | Build, lint, testes, migration e browser |

## Riscos e decisões

- O conteúdo demonstrativo será um universo fictício com textos originais e curtos. Metadados externos fornecem título, autor e capa, mas nenhuma descrição externa entra automaticamente no lore.
- Livros importados sem pack de lore permanecem na biblioteca com estado vazio apropriado.
- Recuperação de senha, IA, busca semântica e repetição espaçada ficam para versões futuras. A revisão MVP registra feedback sem algoritmo de agendamento.
- Um Admin de desenvolvimento é criado apenas por seed explícito com credenciais fornecidas em variáveis de ambiente; nenhum segredo é versionado.
- O `origin` configurado é o repositório do projeto. O trabalho prossegue na branch `main`, vazia no início.

## Aceite

Registro/login, adição de livro, progresso, recall por nome/alias, detalhe com fatos/relações conhecidos, timeline segura, notas privadas, administração e PWA devem funcionar em `http://localhost:3000`. Testes cobrem revelações antes, no ponto e depois, avanço/retrocesso, entidade e alias futuros, posse de recursos e autorização Admin. O layout deve funcionar a partir de 360 px.
