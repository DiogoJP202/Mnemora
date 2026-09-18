# Relatório final de entrega do Mnemora

## Estado da entrega

O MVP do Mnemora está implementado como web app mobile-first e PWA em português. A aplicação oferece cadastro e login, busca externa, cadastro manual de livros, biblioteca, progresso por unidade, exploração segura de lore, Recall textual, notas privadas, revisão simples e um painel administrativo. A regra antisspoiler permanece no backend e impede que dados futuros sejam enviados ao frontend.

Este relatório foi atualizado em 18 de setembro de 2026. O histórico abaixo foi confirmado na branch `main` até a fase 13, commit `f6841c9`, já sincronizado com `origin/main`. A fase 14 corresponde às alterações identificadas abaixo como `este commit`; suas evidências quantitativas serão preenchidas depois da execução dos gates finais.

## Histórico por fase

| Fase | Commit confirmado | Entrega |
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
| 14 — Metadados por edição | este commit | Metadados bibliográficos ricos, ISBN canônico, deduplicação entre providers, confirmação da edição e cache externo de quatro horas |

## Funcionalidades entregues

### Produto para o leitor

- cadastro, login, logout e sessão por cookie HttpOnly;
- catálogo local e busca externa pela Open Library sem chave, com Google Books opcional e resultados diferenciados por edição;
- metadados ricos na busca e confirmação somente leitura antes da importação externa;
- normalização de ISBN e deduplicação entre providers por ISBN-13, ISBN-10 ou provider/identificador;
- importação idempotente e reutilizável pelo par provider/identificador, sem descrição ou sinopse externa potencialmente reveladora;
- cadastro manual de livro privado ao leitor, com título obrigatório e autor/ISBN opcionais;
- biblioteca privada, status de leitura, progresso por unidade e página informativa;
- uso de status, página e notas mesmo quando o livro não possui memory pack;
- títulos neutros para unidades ainda não alcançadas;
- listas de personagens, facções e lugares, detalhe com aliases, fatos e relações conhecidos;
- timeline de eventos conhecidos em ordem cronológica própria;
- Recall textual tolerante a caixa e diacríticos, limitado a campos já revelados;
- notas gerais ou vinculadas, sempre isoladas por usuário;
- revisão determinística de até cinco entidades conhecidas, com “Eu lembro” e “Preciso revisar”;
- navegação responsiva a partir de 360 px, tema light/dark/system e instalação como PWA.

### Produto para administração

- autorização por role `Admin`;
- CRUD de livros, unidades de leitura, entidades, aliases, fatos e relações;
- controles de reordenação de unidades;
- validação de pertença ao livro e de sequência das revelações;
- preview por unidade que usa a mesma projeção segura do leitor;
- pack demonstrativo original e curto, provisionado por seed idempotente;
- criação opcional de Admin por variáveis de ambiente, sem segredo versionado.

### Controles técnicos

- respostas `/api` com `Cache-Control: no-store`;
- mutações protegidas por antiforgery;
- rate limit de autenticação, Recall, integração externa e escritas de notas/revisão;
- cache em memória por quatro horas apenas para resultados públicos de busca externa, sem dados de usuário;
- limite de 200 notas por usuário e livro e retenção das 500 atividades de revisão mais recentes por usuário e livro;
- `401`/`403` para API sem redirects HTML;
- isolamento por sessão para biblioteca, livros manuais, progresso, notas e revisão;
- headers de navegador com CSP, bloqueio de framing, HSTS, `nosniff`, política de referrer e Permissions Policy;
- suporte a proxy reverso por lista explícita de IPs confiáveis;
- banco SQLite versionado por migrations, com o arquivo local fora do Git;
- service worker restrito ao shell público e a assets estáticos.
- CI para testes, lint, builds, contratos da PWA e auditorias de dependências.

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

A fase 14 amplia os contratos de metadados e a experiência de seleção de edição. As evidências quantitativas desta fase devem ser registradas somente após a execução dos respectivos comandos; as tabelas anteriores permanecem como histórico factual das fases já verificadas.

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
2. Busque outro livro por título, autor ou um ISBN formatado. Compare subtítulo, editora, ano, idioma e ISBN, abra a confirmação da edição e verifique que nenhuma importação ocorre antes da confirmação explícita. Depois confirme e valide que status, página e notas ficam disponíveis sem lore.
3. Cadastre manualmente um livro ausente e confirme, com uma segunda conta, que ele não aparece na busca nem pode ser aberto.
4. Sem unidade selecionada no pack demonstrativo, confirme que listas de lore, timeline, Recall e revisão não expõem conteúdo.
5. Marque o Capítulo 1 e confirme que Nara aparece exatamente nessa fronteira.
6. Avance ao Capítulo 2 e confirme o conteúdo liberado; depois retorne ao Capítulo 1 e confirme que ele desaparece.
7. Pesquise um alias ou fato de capítulo futuro e confirme uma lista vazia, sem mensagem que sugira existência futura.
8. Crie uma nota geral e uma nota vinculada; retroceda e confirme que somente a referência ainda permitida permanece visível.
9. Registre as duas opções de revisão e confirme que nenhuma delas libera conteúdo adicional.

### 5. Validar administração e segurança

1. Entre com o Admin local e abra `/admin`.
2. Crie ou edite unidade, entidade, alias, fato e relação.
3. Tente posicionar um fato antes da entidade ou uma relação antes de um participante e confirme a rejeição.
4. Use o preview em duas unidades diferentes e compare com a visão do leitor no mesmo ponto.
5. Em sessão de leitor, confirme `403` para rotas administrativas; sem sessão, confirme `401` nas rotas privadas.
6. Confirme `Cache-Control: no-store` nas respostas `/api` e que Cache Storage não contém entradas de `/api`, `/app` ou `/admin`.

### 6. Executar os gates automatizados

```powershell
dotnet build Mnemora.slnx
dotnet test Mnemora.slnx
node --test tests/pwa/pwa-assets.test.mjs
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
- Um livro importado ou manual traz somente metadados, não um pack de lore. Ele permite status, página e notas; a navegação de lore permanece indisponível até existir curadoria administrativa.
- SQLite é adequado ao uso local e a uma instância pequena; alta concorrência e múltiplas réplicas pedem outro provider relacional.
- O shell público pode funcionar offline, mas a área privada exige rede por decisão de segurança e consistência antisspoiler.
- A curadoria continua responsável por escrever nome, resumo e imagem seguros no primeiro ponto de conhecimento. A aplicação valida a ordem estrutural, não o significado do texto.
- A demonstração gratuita no Render usa armazenamento efêmero; recuperação de desastre e observabilidade centralizada ainda não estão implementadas.

## Conclusão de aceite

O conjunto entregue cobre o escopo funcional planejado e permite que cada leitor mantenha livros além do catálogo curado, sem transformar metadados externos em lore. O critério central foi preservado em todas as superfícies: o frontend recebe somente o conhecimento permitido pela unidade de leitura atual, e qualquer retrocesso é aplicado na requisição seguinte.
