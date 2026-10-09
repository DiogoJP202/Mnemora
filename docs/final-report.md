# Relatório final de entrega do Mnemora

## Estado da entrega

O MVP do Mnemora está implementado como web app mobile-first e PWA em português. A aplicação oferece cadastro e login, busca externa, cadastro manual de livros, gestão completa da estante, solicitação de memory packs, progresso por índice publicado, exploração segura de lore, Recall textual, notas privadas, revisão simples e um painel administrativo com workflow editorial. A regra antisspoiler permanece no backend e impede que rascunhos ou dados futuros sejam enviados ao frontend.

Este relatório foi atualizado em 9 de outubro de 2026. A fase 16 está implementada e validada localmente, incluindo a jornada em navegador. O commit, o push, o CI e o deploy ainda estão pendentes de confirmação nesta revisão.

## Histórico por fase

| Fase | Commit / referência | Entrega |
| --- | --- | --- |
| 00 — Planejamento | `7b36f27` | Plano, arquitetura inicial, riscos, aceite e arquivos ignorados |
| 01 — Fundação | `8127a3a` | Solução .NET, app Next.js, Identity, cookie, antiforgery, Problem Details, SQLite e migration inicial |
| 02 — Domínio | `ea480fd` | Catálogo, unidades hierárquicas, biblioteca, progresso, lore, notas, atividades e migration de domínio |
| 03 — Motor antisspoiler | `3dd07d5` | `KnowledgeReader`, fronteira inclusiva, DTOs seguros e testes de visibilidade/endpoints |
| 04 — Administração | `868104f` | CRUD de livros, unidades e lore, relações, reordenação validada, preview seguro e seed original |
| 05 — Experiência do leitor | `4a25e3b` | Landing, autenticação, catálogo, Google Books opcional, biblioteca, página do livro e progresso |
| 06 — Exploração do lore | `9b5c049` | Personagens, facções, lugares, detalhe e timeline filtrados pelo progresso |
| 07 — Recall | `b976d0e` | Busca por nomes, descrições, aliases, fatos e pistas conhecidos, com normalização de diacríticos |
| 08 — Notas e revisão | `a8dea24` | Notas privadas, vínculo seguro a entidades/unidades, revisão de até cinco entidades e feedback |
| 09 — PWA e acabamento | `684550e` | Temas light/dark/system, responsividade, acessibilidade, metadata, ícones, manifesto e cache seguro |
| 10 — Qualidade e entrega | `e1f2347` | Revisão completa, documentação técnica, instruções de operação, limitações e relatório final |
| 11 — Preparação do deploy | `85b2c2a` | Container de produção, Blueprint do Render, health check e documentação operacional |
| 12 — Demonstração gratuita | `9eb427b` | Render gratuito sem cartão, aviso de dados efêmeros e seed sem Admin obrigatório |
| 13 — Livros do leitor | `f6841c9` | Open Library sem chave, Google Books opcional, cadastro manual privado e experiência para livros sem pack |
| 14 — Metadados por edição | `24aaaac` | Metadados bibliográficos ricos, ISBN canônico, deduplicação entre providers, confirmação da edição e cache externo de quatro horas |
| Manutenção pós-fase 14 | `8fed8fa` | Atualização do Next.js para o patch de segurança 16.3.8 |
| 15 — Gestão da estante | `b58552c` | Status e progresso atômicos, limpeza do ponto, metadados da edição na estante/detalhe e remoção isolada com confirmação |
| 16 — Memory packs editoriais | Implementação local; commit pendente | Índice e lore com publicação independente, solicitações do leitor, fila administrativa e importação opcional de sumário da Open Library |

## Funcionalidades entregues

### Produto para o leitor

- cadastro, login, logout e sessão por cookie HttpOnly;
- catálogo local e busca externa pela Open Library sem chave, com Google Books opcional e resultados diferenciados por edição;
- metadados ricos na busca e confirmação somente leitura antes da importação externa;
- normalização de ISBN e deduplicação entre providers pelo ISBN canônico, preferindo ISBN-13, ou pelo provider/identificador quando não há ISBN válido;
- importação idempotente e reutilizável pelo par provider/identificador, sem descrição ou sinopse externa potencialmente reveladora;
- cadastro manual de livro privado ao leitor, com título obrigatório e autor/ISBN opcionais;
- biblioteca privada com os quatro status em livros com ou sem pack;
- gravação única de status, unidade e página, com opção de limpar o ponto e revogar lore sem remover o livro;
- subtítulo e metadados persistidos da edição nos cards e no detalhe;
- remoção com confirmação acessível, cancelável pelo botão ou por `Escape`, que apaga progresso, notas e histórico de revisão somente da conta atual;
- uso de status, página e notas mesmo quando o livro não possui memory pack;
- solicitação idempotente de memory pack para um livro compartilhado da biblioteca;
- progresso por unidade assim que o índice estiver publicado, sem antecipar o lore ainda em preparação;
- títulos neutros para unidades ainda não alcançadas;
- listas de personagens, facções e lugares, detalhe com aliases, fatos e relações conhecidos;
- timeline de eventos conhecidos em ordem cronológica própria;
- Recall textual tolerante a caixa e diacríticos, limitado a campos já revelados;
- notas gerais ou vinculadas, sempre isoladas por usuário;
- revisão determinística de até cinco entidades conhecidas, com “Eu lembro” e “Preciso revisar”;
- navegação responsiva a partir de 360 px, tema light/dark/system e instalação como PWA.

### Produto para administração

- autorização por role `Admin`;
- fila editorial ordenada por solicitações, com origem bibliográfica e contagens de conteúdo;
- estados independentes `None`, `Draft` e `Published` para índice e lore;
- preparação, publicação e retirada controladas, com validação antes de liberar conteúdo;
- prévia e importação idempotente do sumário opcional de edições exatas da Open Library, com hierarquia, rótulos neutros e procedência;
- CRUD de livros, unidades de leitura, entidades, aliases, fatos e relações;
- controles de reordenação de unidades;
- validação de pertença ao livro e de sequência das revelações;
- preview por unidade que usa a mesma projeção segura do leitor;
- pack demonstrativo original e curto, provisionado por seed idempotente;
- criação opcional de Admin por variáveis de ambiente, sem segredo versionado.

### Controles técnicos

- respostas `/api` com `Cache-Control: no-store`;
- mutações protegidas por antiforgery;
- rate limit de autenticação, Recall, integração externa e escritas de biblioteca, notas e revisão;
- cache em memória por quatro horas apenas para resultados públicos de busca externa, sem dados de usuário;
- limite de 200 notas por usuário e livro e retenção das 500 atividades de revisão mais recentes por usuário e livro;
- `401`/`403` para API sem redirects HTML;
- isolamento por sessão para biblioteca, livros manuais, progresso, notas e revisão;
- bloqueio de conteúdo em rascunho antes da montagem dos DTOs e revalidação de acesso nos endpoints administrativos filhos;
- headers de navegador com CSP, bloqueio de framing, HSTS, `nosniff`, política de referrer e Permissions Policy;
- suporte a proxy reverso por lista explícita de IPs confiáveis;
- banco SQLite versionado por migrations, com o arquivo local fora do Git;
- service worker restrito ao shell público e a assets estáticos.
- CI para testes, lint, builds, contratos da PWA e auditorias de dependências.
- remoção transacional com preservação do catálogo compartilhado e dos dados de outras contas;
- limpeza conservadora de livro privado somente quando ele pertence ao leitor, fica órfão e não possui conteúdo.

## Evidências de validação da entrega inicial

As verificações finais da fase 10 produziram estes resultados:

| Verificação | Resultado observado | Cobertura principal |
| --- | --- | --- |
| `dotnet test Mnemora.slnx` | 28 testes aprovados | Domínio, integridade, autenticação, antiforgery, persistência das chaves de proteção, seed administrativo, biblioteca, administração, fronteiras de lore, Recall, notas e revisão |
| `node --test tests/pwa/pwa-assets.test.mjs` | 4 testes aprovados | Manifesto, dimensões dos ícones, pré-cache público e exclusão de rotas privadas |
| `dotnet build Mnemora.slnx` | Build aprovado | Solução e referências entre as quatro camadas |
| `npm run lint` em `apps/web` | Aprovado | ESLint/Next.js |
| `npm run build` em `apps/web` | Build de produção aprovado | Rotas App Router, manifesto e bundle do frontend |
| Jornada em navegador | Aprovada em 360 px, 390 px e desktop | Landing, autenticação, biblioteca, progresso, lore, Recall, notas, revisão e retrocesso |
| Tema | Aprovado em light e dark | Persistência/alternância visual e contraste dos estados inspecionados |
| Auditoria WCAG automatizada | Zero violações nas páginas inspecionadas | Regras WCAG A/AA disponíveis na ferramenta usada |
| Cache da PWA | Nenhuma consulta ou gravação para `/api`, `/app` e `/admin` | Simulação do service worker e inspeção do comportamento do cache privado |
| Build servido em `localhost:3000` | Landing e login renderizados sem erro de console ou overlay | CSP de produção, navegação, ausência de overflow e headers de segurança |
| Migration e seed em banco limpo | Aprovados em duas execuções | Aplicação das migrations, criação do pack/Admin e idempotência do seed |
| Auditorias de dependências | 0 vulnerabilidades npm e NuGet | Dependências diretas e transitivas disponíveis aos gerenciadores |
| Busca de segredos no diff | Nenhum segredo real detectado | Arquivos versionados e alterações da fase 10 |

Os 28 testes .NET incluem os casos críticos pedidos no aceite: antes, exatamente no ponto e depois da revelação; ausência de progresso; entidade, alias, fato, relação e evento futuros; avanço e retrocesso; título neutro; termo futuro no Recall; biblioteca e notas de outro usuário; limite de cinco itens na revisão; quotas de notas e histórico de revisão; role Admin; consistência de reordenação; preview com projeção segura; rejeição de escrita sem antiforgery; persistência das chaves de proteção quando configurada; e impossibilidade de promover uma conta existente com senha divergente durante o seed.

A fase 13 acrescenta testes de providers, importação por provider, privacidade do cadastro manual, deduplicação e uso de livros sem pack. Os totais da tabela anterior permanecem como registro da fase 10.

A fase 14 amplia os contratos de metadados e a experiência de seleção de edição. As tabelas anteriores permanecem como histórico factual das fases já verificadas.

### Evidências da fase 13

| Verificação | Resultado observado | Cobertura principal |
| --- | --- | --- |
| `dotnet test Mnemora.slnx --configuration Release --no-restore` | 41 testes aprovados | Providers, importação idempotente, ISBN, privacidade, quotas, upgrade da migration, administração e regressões antisspoiler |
| `npm run lint` e `npm run build` em `apps/web` | Aprovados | Componentes do leitor, rotas App Router e tipos do contrato atualizado |
| `node --test tests/pwa/pwa-assets.test.mjs` | 4 testes aprovados | Manifesto, ícones e limites do cache da PWA |
| `dotnet ef migrations has-pending-model-changes` | Nenhuma alteração pendente | Correspondência entre o modelo EF Core e a migration da fase |
| Jornada em navegador local | Aprovada em desktop e 360 px | Busca real na Open Library, importação, cadastro manual, acompanhamento, notas, privacidade visual e ausência de overflow/erros de console |
| Auditorias npm e NuGet | 0 vulnerabilidades conhecidas | Dependências diretas e transitivas disponíveis aos gerenciadores |
| `git diff --check` e busca de segredos | Aprovados | Integridade do diff e ausência de credenciais versionadas |

### Evidências da fase 14

| Verificação | Resultado observado | Cobertura principal |
| --- | --- | --- |
| `dotnet test Mnemora.slnx` | 53 testes aprovados | Parsing dos providers, ISBN canônico, edições distintas, fallback parcial, cache, importação idempotente, compatibilidade com Work legado e regressões existentes |
| `dotnet build Mnemora.slnx` | Build aprovado, sem avisos | Solução e contrato expandido entre Application, Infrastructure e API |
| `npm run lint` e `npm run build` em `apps/web` | Aprovados | Next.js 16.3.8, TypeScript, cards bibliográficos e confirmação da edição |
| `node --test tests/pwa/pwa-assets.test.mjs` | 4 testes aprovados | Manifesto, ícones e exclusão de dados privados do service worker |
| `dotnet ef migrations has-pending-model-changes` | Nenhuma alteração pendente | Reutilização das colunas existentes em `Book`, sem migration artificial |
| Jornada em navegador local | Aprovada em desktop e 360 px | Busca real por título e ISBN formatado, metadados da edição, ausência de importação antes da confirmação, inclusão explícita, foco acessível, ausência de overflow e console sem erros |
| Auditorias npm e NuGet | 0 vulnerabilidades conhecidas | Dependências diretas e transitivas disponíveis aos gerenciadores |

### Evidências da fase 15

| Verificação | Resultado observado | Cobertura principal |
| --- | --- | --- |
| `dotnet test Mnemora.slnx --no-restore` | 56 testes aprovados | Atomicidade, unidade de outro livro, preservação de timestamps, antiforgery, isolamento entre contas, catálogo compartilhado e remoção conservadora de livro privado |
| `dotnet build Mnemora.slnx --no-restore` | Build aprovado | API, domínio, infraestrutura e testes após a mudança de ciclo de vida |
| `npm run lint`, `npx tsc --noEmit` e `npm run build` | Aprovados | Componentes da estante, tipos, acessibilidade e build de produção do Next.js 16.3.8 |
| `node --test tests/pwa/pwa-assets.test.mjs` | 4 testes aprovados | Manifesto, ícones e exclusão de dados privados do service worker |
| `dotnet ef migrations has-pending-model-changes` | Nenhuma alteração pendente | Fase sem alteração de esquema |
| Jornada em navegador local | Aprovada em desktop e 360 px | Metadados, ausência de overflow, status, limpeza do ponto, rótulos novamente neutros, confirmação, cancelamento e foco por `Escape` |
| `git diff --check` e busca de segredos | Aprovados | Integridade do diff e ausência de credenciais versionadas |

Os testes rejeitam exclusão sem antiforgery, removem somente as memórias do leitor atual e preservam a segunda conta. Livros `Curated` e `Imported` continuam no catálogo; um `Private` só desaparece quando está órfão e sem conteúdo. A atualização combinada valida todos os campos antes de persistir, mantém campos e timestamps diante de payload inválido, dá precedência ao status explícito e não redefine a data de conclusão em um salvamento repetido. A suíte antisspoiler existente confirma que limpar a unidade revoga lore e títulos antes liberados.

### Evidências da fase 16

| Verificação | Resultado observado | Cobertura principal |
| --- | --- | --- |
| `dotnet test Mnemora.slnx --configuration Release` | 63 testes aprovados | Solicitação idempotente, rascunhos invisíveis, publicação separada, retirada de conteúdo, isolamento de livros privados, importação integral de índice e fronteiras de 200/201 itens, além das regressões existentes |
| `dotnet build Mnemora.slnx` | Build aprovado | API, domínio, infraestrutura, migration e testes do workflow editorial |
| `npm run lint`, `npx tsc --noEmit` e `npm run build` | Aprovados | Estados do leitor, fila e workflow Admin, tipos e build de produção |
| `node --test tests/pwa/pwa-assets.test.mjs` | 4 testes aprovados | Manifesto, ícones e exclusão de dados privados do service worker |
| `dotnet ef migrations has-pending-model-changes` | Nenhuma alteração pendente | Correspondência entre o modelo e a migration `MemoryPackWorkflow` |
| `node --test tests/security/audit-npm.test.mjs` e `node scripts/audit-npm.mjs` | 36 testes e gate real aprovados | Exceção exata de desenvolvimento, versões/grafo, bloqueio de novos alertas, falhas de auditoria e expiração em 9/11/2026 UTC |
| `npm audit --omit=dev --audit-level=high` | Sem vulnerabilidades no ambiente de execução | Atualização de `sharp` para 0.35.5 e `source-map-js` para 1.2.2 |
| Auditoria npm completa | Aviso `high` de desenvolvimento conhecido, sem patch compatível | `GHSA-vfj7-8cjw-p6xm` em `braces` 3.0.3 pela cadeia ESLint; exceção limitada às cinco versões e à classificação `dev-only`, com expiração no início de 9 de novembro de 2026 (UTC) |
| Jornada em navegador local com Playwright | Aprovada em desktop de 1280 px e mobile de 360 px, em light e dark | Cadastro, busca real, confirmação/importação, solicitação persistida, notas sem pack, fila Admin, preparação, lote de unidades e publicação do índice antes do lore |
| Rotas de exploração e preservação de progresso | Aprovadas, sem erros de página, overlay ou overflow | `characters`, `factions`, `locations`, `timeline`, `recall`, `notes` e `review`; capítulo, página e status preservados ao retirar e republicar o índice |
| Commit, push e CI da fase | Pendentes nesta revisão | Publicação do diff revisado e execução dos checks remotos |
| Deploy da fase | Pendente nesta revisão | Migração e disponibilidade do workflow na instância pública |

A suíte comprova que unidades em rascunho não tornam o índice público e que entidades em rascunho não tornam o pack disponível. O índice pode ser publicado primeiro e usado para progresso, enquanto o lore permanece indisponível. A publicação posterior do lore libera o conteúdo sem exigir que o leitor remova e adicione o livro novamente; a retirada volta a bloquear as consultas. Também há cobertura para a unicidade do pedido por usuário/livro, para a procedência e hierarquia do sumário importado e para GUIDs privados inacessíveis pelas rotas administrativas filhas.

A migration de atualização conserva o conteúdo editorial preexistente como publicado e mantém livros sem conteúdo em `None`. Isso permite aplicar a nova política sem recolher o pack demonstrativo nem liberar novas importações automaticamente. O sumário opcional da Open Library entra somente em rascunho; dados ausentes, IDs de obra e providers diferentes não geram unidades inventadas.

A jornada foi executada em `http://localhost:3001`, encaminhando a API para `http://localhost:5199`, porque a porta `5100` estava ocupada por outro projeto. A busca real por `Dom Casmurro` permitiu confirmar a edição, importar o livro, solicitar um pack e conferir que o pedido acompanhava a navegação entre telas. As notas funcionaram antes de existir lore. O Admin encontrou o pedido, iniciou a preparação e consultou o sumário da edição; a fonte respondeu corretamente que não havia sumário. O fluxo prosseguiu com criação de unidades em lote e publicação do índice, permitindo marcar capítulo antes de publicar conhecimento.

Os achados da verificação foram corrigidos: o estado do pedido agora acompanha os dados atualizados ao navegar; a unidade é omitida do payload durante a revisão editorial para preservar o ponto registrado; e o texto acessível da navegação foi contido para eliminar overflow horizontal. Ao retirar e republicar o índice, capítulo, página e status permaneceram preservados. Os testes do provider confirmam que uma fonte com 200 itens válidos é integral e que 201 itens provocam rejeição, sem truncamento; a API traduz esse excesso para `422`.

A auditoria de execução e a auditoria completa são evidências distintas. O gate `scripts/audit-npm.mjs` valida a exceção específica de desenvolvimento e rejeita novas vulnerabilidades `high`/`critical`, mudanças na cadeia ou expiração; ele não declara a auditoria completa livre de achados. A decisão e o procedimento para remover a exceção estão em [dependency-security.md](dependency-security.md).

A afirmação “zero violações” se limita às páginas e estados visitados pela auditoria automatizada. Ela não substitui avaliação manual completa com leitores de tela, diferentes tecnologias assistivas ou uma auditoria de conteúdo editorial.

## Roteiro de aceite reproduzível

### 1. Preparar dependências

Pré-requisitos: Node.js 24 LTS, npm, .NET SDK 10.0.400 (ou patch compatível aceito por `global.json`) e Git.

```powershell
git clone https://github.com/DiogoJP202/Mnemora.git
Set-Location Mnemora
dotnet tool restore
Set-Location apps/web
npm ci
Set-Location ../..
```

### 2. Criar o banco e o conteúdo demonstrativo

Para criar também um Admin local, defina credenciais somente no processo atual:

```powershell
$env:ADMIN_EMAIL = "admin@example.local"
$env:ADMIN_PASSWORD = "Escolha-Uma-Senha-Local-2026"
dotnet tool run dotnet-ef database update --project src/Mnemora.Infrastructure --startup-project src/Mnemora.Api
dotnet run --project src/Mnemora.Api -- --seed
```

O último comando mantém a API em `http://localhost:5100`. A senha precisa ter pelo menos 10 caracteres, uma letra maiúscula, uma minúscula e um dígito. Sem `ADMIN_EMAIL` e `ADMIN_PASSWORD`, o pack demonstrativo ainda é criado, mas nenhum Admin é provisionado.

### 3. Iniciar o frontend

Em outro terminal:

```powershell
Set-Location apps/web
npm run dev
```

Abra `http://localhost:3000`. O rewrite padrão encaminha `/api` para a API local. Se as portas mudarem, use `API_BASE_URL` e `Mnemora__WebOrigin` conforme `.env.example`; no backend, edite o launch profile ou use `dotnet run --no-launch-profile --project src/Mnemora.Api --urls http://localhost:5199`, pois o perfil padrão fixa `5100`.

### 4. Validar a jornada do leitor

1. Cadastre um leitor e adicione `O Arquivo da Neblina` à biblioteca.
2. Busque outro livro por título, autor ou um ISBN formatado. Compare subtítulo, editora, ano, idioma e ISBN, abra a confirmação da edição e verifique que nenhuma importação ocorre antes da confirmação explícita. Depois confirme, valide que status, página e notas ficam disponíveis sem lore e registre uma solicitação de memory pack.
3. Cadastre manualmente um livro ausente e confirme, com uma segunda conta, que ele não aparece na busca nem pode ser aberto.
4. Confira os metadados persistidos na estante e no detalhe e troque o status em um livro com pack e outro sem pack.
5. Sem unidade selecionada no pack demonstrativo, confirme que listas de lore, timeline, Recall e revisão não expõem conteúdo.
6. Marque o Capítulo 1 e confirme que Nara aparece exatamente nessa fronteira; depois escolha “Nenhuma — limpar ponto de leitura” e confirme que o lore some e os títulos voltam aos rótulos neutros.
7. Avance ao Capítulo 2 e confirme o conteúdo liberado; depois retorne ao Capítulo 1 e confirme que ele desaparece.
8. Pesquise um alias ou fato de capítulo futuro e confirme uma lista vazia, sem mensagem que sugira existência futura.
9. Crie uma nota geral e uma nota vinculada; retroceda e confirme que somente a referência ainda permitida permanece visível.
10. Registre as duas opções de revisão e confirme que nenhuma delas libera conteúdo adicional.
11. Abra a confirmação de remoção, cancele pelo botão e por `Escape`, confirme o retorno de foco e então remova o livro. Com uma segunda conta, confirme que a estante e as memórias dela permanecem intactas.

### 5. Validar administração e segurança

1. Entre com o Admin local e abra `/admin`.
2. Abra o livro solicitado na fila, coloque o pack em preparação e confirme que o leitor vê o estado “em preparação”, sem receber o rascunho.
3. Se a edição da Open Library oferecer sumário, revise a prévia e importe-a; caso contrário, crie as unidades manualmente. Publique o índice e confirme que o leitor já pode marcar progresso.
4. Crie ou edite entidade, alias, fato e relação, use o preview em duas unidades e compare com a visão segura do leitor.
5. Tente posicionar um fato antes da entidade ou uma relação antes de um participante e confirme a rejeição; então publique o lore e verifique que o pedido foi encerrado.
6. Retire o lore e depois o índice e confirme que a disponibilidade é revogada. Em sessão de leitor, confirme `403` para rotas administrativas; sem sessão, confirme `401` nas rotas privadas.
7. Confirme `Cache-Control: no-store` nas respostas `/api` e que Cache Storage não contém entradas de `/api`, `/app` ou `/admin`.

### 6. Executar os gates automatizados

```powershell
dotnet build Mnemora.slnx
dotnet test Mnemora.slnx
node --test tests/pwa/pwa-assets.test.mjs
node scripts/audit-npm.mjs
node --test tests/security/audit-npm.test.mjs
Set-Location apps/web
npm run lint
npm run build
```

Para verificar a PWA em condições reais de service worker, execute o build de produção com `npm run start`; o registro é desabilitado durante `npm run dev`.

## Limitações e trabalho futuro

- Recuperação de senha é informativa no MVP; não há envio de e-mail nem fluxo de redefinição.
- Busca semântica, geração por IA e enriquecimento automático de lore não estão implementados.
- A revisão registra feedback, mas não agenda cartões nem implementa repetição espaçada.
- A Open Library funciona sem chave; o Google Books só participa quando `GOOGLE_BOOKS_API_KEY` está configurada.
- Número de páginas, categorias e rótulo de edição são usados na busca e confirmação, mas não são persistidos no modelo atual de `Book`.
- Um livro importado ou manual traz somente metadados, não um pack de lore. Uma edição Open Library compartilhada pode oferecer um sumário para iniciar o índice, mas a disponibilidade varia e seus títulos ainda exigem revisão administrativa. Livros manuais permanecem privados e fora da fila editorial.
- Personagens, facções, lugares, fatos, relações, eventos e pontos de revelação não vêm dos providers atuais e continuam dependendo de autoria e curadoria humanas.
- SQLite é adequado ao uso local e a uma instância pequena; alta concorrência e múltiplas réplicas pedem outro provider relacional.
- O shell público pode funcionar offline, mas a área privada exige rede por decisão de segurança e consistência antisspoiler.
- A curadoria continua responsável por escrever nome, resumo e imagem seguros no primeiro ponto de conhecimento. A aplicação valida a ordem estrutural, não o significado do texto.
- A demonstração gratuita no Render usa armazenamento efêmero; recuperação de desastre e observabilidade centralizada ainda não estão implementadas.

## Conclusão de aceite

O conjunto entregue cobre o escopo funcional planejado e permite que cada leitor adicione, acompanhe, solicite conteúdo e remova livros com isolamento por usuário, sem transformar metadados externos em lore. O índice pode chegar antes do conhecimento, mas nenhum rascunho é confundido com conteúdo disponível. O critério central foi preservado em todas as superfícies: o frontend recebe somente o conhecimento publicado e permitido pela unidade de leitura atual, e qualquer retrocesso, limpeza ou retirada editorial é aplicado na requisição seguinte.
