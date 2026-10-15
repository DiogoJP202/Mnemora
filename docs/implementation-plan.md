# Plano de implementação do Mnemora

Mnemora é um companion de leitura mobile-first. A consulta deve recuperar apenas o que o leitor já encontrou, sem transmitir conteúdo futuro ao navegador. O nome do produto, namespaces, banco, PWA e documentação é Mnemora. A interface usa português; a tagline pública é “Remember the story. Not the spoilers.”

## Arquitetura

Monorepo com `apps/web` (Next.js App Router, React, TypeScript, Tailwind) e `src/Mnemora.Api`, `src/Mnemora.Application`, `src/Mnemora.Domain`, `src/Mnemora.Infrastructure` (ASP.NET Core, EF Core, Identity, SQLite). A API REST vive em `/api`. O Next.js encaminha `/api/*` ao backend para manter a origem do navegador única. Toda resposta privada recebe `Cache-Control: no-store`. Cookies de autenticação são HttpOnly, SameSite Lax e Secure em produção; operações de escrita requerem antiforgery.

O banco `mnemora.db` usa migrations EF e permanece fora do Git. As tabelas principais são Book, ReadingUnit, UserBook, LoreEntity, EntityAlias, LoreFact, LoreRelation, UserNote, UserRecallActivity e MemoryPackRequest. ReadingUnit tem hierarquia e ordem absoluta por livro; o progresso usa a unidade, nunca a página. Alias, fato e relação têm unidade de revelação. Eventos podem ter ordem cronológica distinta da ordem de leitura. Book mantém publicação independente do índice e do lore; MemoryPackRequest registra um pedido pendente por usuário/livro compartilhado.

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

### Evoluções pós-MVP

| Fase | Entrega | Gate mínimo |
| --- | --- | --- |
| 11 | Container e Blueprint para publicação no Render | Build do container e health check |
| 12 | Demonstração gratuita com dados efêmeros | Deploy sem credenciais e aviso operacional |
| 13 | Open Library, cadastro manual privado e livros sem pack | Providers, isolamento e jornada de adição |
| 14 | Metadados bibliográficos por edição e confirmação da importação | Contratos, deduplicação, cache e jornada por ISBN |
| 15 | Gestão completa da estante: status em todos os livros, atualização atômica, limpeza do ponto, metadados persistidos e remoção segura | Atomicidade e isolamento, build/lint e jornada desktop/360 px |
| 16 | Workflow editorial de memory packs, índice independente, solicitações do leitor e importação opcional de sumário da Open Library | Estados de publicação, ausência de vazamento por rascunho, idempotência, autorização, migration de upgrade, builds e jornada desktop/360 px |
| 17 | Importação e exportação de pacotes editoriais JSON v1 completos, modelo por edição e validação sem gravação | Contrato estrito, referências e fronteiras de revelação, atomicidade, idempotência, conflito sem substituição, autorização e jornada Admin |
| 18 | Contratos da geração assistida e separação das fontes por capítulo | Edição do servidor, índice confirmado, lacunas e limites, alvo compartilhado vazio |
| 19 | Provedor Groq e montagem incremental com evidências | Schema estrito, prefixo conhecido, apresentações congeladas e equivalência das projeções |
| 20 | Sessões temporárias e API administrativa | Admin/CSRF, posse, revisões, idempotência, concorrência, quotas e cancelamento |
| 21 | Jornada de geração e transferência no Admin | Fontes em lote, progresso, revisão, downloads, parcial confirmado e importação em rascunho |
| 22 | Regressões, avaliação controlada e documentação | Suíte completa, auditorias, jornada pela origem Next.js, CI sem API real e ausência de migration |

As fases 18 a 22 seguem o [plano de geração assistida](assisted-generation-plan.md). A operação entregue está descrita em [assisted-generation.md](assisted-generation.md).

### Aceite da fase 16

- O leitor registra um pedido idempotente para livro compartilhado da sua biblioteca, e o Admin acompanha a demanda na fila editorial.
- O índice pode ser publicado antes do lore e já permitir progresso; unidades em rascunho não chegam ao leitor.
- O pack completo exige índice publicado e lore publicado com entidades, mantendo as fronteiras antisspoiler existentes.
- A retirada de publicação revoga a disponibilidade na próxima consulta; retirar o índice também recolhe o lore.
- Um sumário disponível em uma edição exata da Open Library pode ser revisado e importado com hierarquia, rótulos neutros e procedência. Sem sumário, o fluxo permite autoria manual.
- Notas gerais funcionam em livros sem pack, enquanto vínculos a unidades ou entidades respeitam publicação e progresso.
- O upgrade preserva conteúdo publicado anterior e mantém novas importações sem conteúdo editorial; as rotas administrativas filhas não dão acesso a livros privados.

### Aceite da fase 17

- O Admin exporta o conteúdo completo por slugs ou baixa um modelo vazio ligado à edição, sem GUIDs, estados de publicação ou dados dos leitores.
- O JSON v1 inclui unidades hierárquicas, entidades, aliases, fatos, pistas, relações e cronologia de eventos; campos desconhecidos, enums inválidos e referências incoerentes são recusados.
- A validação confere edição, limites e pontos de revelação e devolve contagens e problemas sem gravar. A importação valida novamente antes da gravação atômica.
- Um pacote novo só entra em livro compartilhado sem conteúdo editorial e com estados `None` ou `Draft`; índice e lore são criados em `Draft`. Conteúdo diferente existente retorna conflito sem substituição.
- Reenviar um pacote idêntico é uma operação sem gravação que preserva IDs e estados. Metadados bibliográficos, progresso, notas, atividades e solicitações pessoais não são substituídos.
- O arquivo completo, a validação e a importação ficam restritos ao Admin; livros privados não são expostos. Escritas mantêm antiforgery e respostas sem cache.
- O Admin revisa o rascunho pela prévia segura antes de publicar índice e lore separadamente. Importar não aumenta o conhecimento disponível para leitores.
- A documentação inclui o [contrato v1](memory-pack-format.md) e um [exemplo fictício original](examples/memory-pack-v1.json), sem tratar textos gerados como fatos de livros reais.

## Riscos e decisões

- O conteúdo demonstrativo será um universo fictício com textos originais e curtos. Metadados externos fornecem título, autor e capa, mas nenhuma descrição externa entra automaticamente no lore.
- Livros importados sem pack de lore permanecem na biblioteca com estado apropriado e podem receber um índice publicado antes do conhecimento.
- Sumários externos são apenas uma ajuda para criar unidades: a disponibilidade varia por edição, e personagens, facções, relações, eventos e pontos de revelação continuam sob curadoria humana.
- Um arquivo JSON completo transporta conteúdo curado; não busca nem inventa lore. O pacote precisa corresponder à edição, e limites estruturais não substituem a revisão da segurança dos nomes, resumos, imagens, pistas e rótulos.
- Recuperação de senha, respostas por IA ao leitor, busca semântica e repetição espaçada ficam para versões futuras. A geração editorial assistida é opcional e depende de fontes e revisão do Admin. A revisão MVP registra feedback sem algoritmo de agendamento.
- Um Admin de desenvolvimento é criado apenas por seed explícito com credenciais fornecidas em variáveis de ambiente; nenhum segredo é versionado.
- O `origin` configurado é o repositório do projeto. O trabalho prossegue na branch `main`, vazia no início.
- Limpar o ponto de leitura revoga o lore, mas mantém o livro e suas memórias pessoais; remover o livro é definitivo para progresso, notas e histórico de revisão daquela conta e exige confirmação explícita.
- A exclusão de um `Book` é conservadora: registros curados ou importados sobrevivem à remoção da estante, e um livro privado só é eliminado quando pertence ao leitor, fica órfão e não possui conteúdo editorial.

## Aceite

Registro/login, adição de livro, progresso, recall por nome/alias, detalhe com fatos/relações conhecidos, timeline segura, notas privadas, administração e PWA devem funcionar em `http://localhost:3000`. Testes cobrem revelações antes, no ponto e depois, avanço/retrocesso, entidade e alias futuros, posse de recursos e autorização Admin. O layout deve funcionar a partir de 360 px.
