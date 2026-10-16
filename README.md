# Mnemora

> **Remember the story. Not the spoilers.**

Mnemora é um companion de leitura mobile-first para recordar personagens, lugares, facções, relações e acontecimentos sem ultrapassar o ponto atual do leitor. A aplicação também oferece biblioteca pessoal com gestão de status e remoção segura, busca de livros por título, autor ou ISBN, cadastro manual, progresso por unidade de leitura que pode ser limpo, notas privadas, revisão curta de entidades conhecidas, solicitação de memory packs e uma área administrativa com preparação, geração assistida por IA, importação/exportação JSON e publicação.

O projeto é uma PWA em português (`pt-BR`) e usa um universo fictício original no conteúdo demonstrativo.

## Como o antisspoiler funciona

Cada unidade de leitura tem uma ordem absoluta no livro. Entidades possuem um primeiro ponto conhecido; aliases, fatos e relações possuem seus próprios pontos de revelação. Em toda consulta privada, o backend combina o livro da biblioteca com a unidade atual do usuário e filtra o conhecimento no banco **antes** de montar o DTO enviado ao navegador.

As regras principais são:

- sem uma unidade de progresso, nenhum lore fica visível;
- a fronteira é inclusiva: conteúdo revelado na unidade atual já pode aparecer;
- ao retroceder, fatos, aliases, relações, eventos e notas vinculadas a referências posteriores deixam de aparecer;
- uma relação exige que o ponto da relação e as duas entidades envolvidas estejam liberados;
- a busca e o Recall consultam somente nomes, resumos, aliases, fatos e pistas já revelados;
- unidades futuras usam um rótulo neutro em vez de um título potencialmente revelador;
- a página atual é apenas uma referência pessoal e não controla visibilidade;
- respostas da API usam `Cache-Control: no-store`, e o service worker ignora `/api`, `/app` e `/admin`.

A implementação e as invariantes estão detalhadas em [docs/spoiler-system.md](docs/spoiler-system.md) e [docs/domain-model.md](docs/domain-model.md).

## Arquitetura e stack

O repositório é um monorepo com um frontend Next.js e um monólito modular ASP.NET Core:

```text
Navegador / PWA
       │ mesma origem: /api/*
       ▼
Next.js App Router ─────► ASP.NET Core Minimal API
                              │
              Application / Domain / Infrastructure
                              │
                         EF Core + SQLite
```

Versões registradas no projeto:

| Camada | Tecnologia |
| --- | --- |
| Web | Next.js 16.3.8, React 19.3.0, TypeScript 5.9.3, Tailwind CSS 4.3.3 |
| API | ASP.NET Core em .NET 10 (`net10.0`) |
| SDK .NET | 10.0.400, definido em `global.json` |
| Persistência | EF Core 10.0.12 e SQLite |
| Autenticação | ASP.NET Core Identity com cookie HttpOnly e antiforgery |
| Testes | xUnit 2.9.3, Microsoft.AspNetCore.Mvc.Testing 10.0.12 e `node:test` |
| Runtime web | Node.js 24, definido em `apps/web/package.json` |

O navegador acessa a API pela mesma origem do Next.js. Em desenvolvimento, o rewrite de `/api/*` aponta para `http://localhost:5100`. Cookies usam `SameSite=Lax` e ficam `Secure` em produção; toda escrita em `/api` exige o token obtido em `GET /api/auth/csrf`. A role `Admin` protege os endpoints de edição.

Mais contexto está em [docs/architecture.md](docs/architecture.md).

## Pré-requisitos

- Git;
- Node.js 24 LTS e npm;
- .NET SDK 10.0.400, ou uma versão de patch compatível aceita por `global.json`;
- um navegador atual para testar instalação da PWA.

SQLite é usado por meio do provider do EF Core; não é necessário instalar um servidor de banco.

## Clone e instalação

```powershell
git clone https://github.com/DiogoJP202/Mnemora.git
Set-Location Mnemora
dotnet tool restore
Set-Location apps/web
npm ci
Set-Location ../..
```

`npm ci` usa o lockfile em `apps/web/package-lock.json`. O manifesto local do EF instala `dotnet-ef` 10.0.12.

## Configuração

O arquivo [.env.example](.env.example) documenta as variáveis aceitas, mas não é carregado automaticamente pelo PowerShell nem pelo .NET.

### Backend

O backend usa a configuração padrão do ASP.NET Core. Valores locais não sensíveis podem ficar em `src/Mnemora.Api/appsettings.Development.json`; credenciais devem ser fornecidas como variáveis do processo que inicia a API.

| Variável | Padrão | Finalidade |
| --- | --- | --- |
| `ConnectionStrings__Default` | `Data Source=mnemora.db` | Conexão SQLite |
| `Mnemora__WebOrigin` | `http://localhost:3000` | Origem autorizada pelo CORS |
| `Mnemora__KnownProxies__0` | vazio | IP do proxy reverso confiável que envia `X-Forwarded-For` e `X-Forwarded-Proto` |
| `Mnemora__DataProtectionKeysPath` | vazio | Diretório persistente para as chaves que protegem cookies e tokens |
| `ASPNETCORE_URLS` | perfil local em `http://localhost:5100` | URL da API |
| `ADMIN_EMAIL` | vazio | E-mail do Admin criado durante o seed |
| `ADMIN_PASSWORD` | vazio | Senha do Admin criado durante o seed |
| `GOOGLE_BOOKS_API_KEY` | vazio | Acrescenta Google Books à busca e importação externas |
| `OPEN_LIBRARY_CONTACT` | URL do repositório | E-mail ou URL de contato enviado no `User-Agent` das consultas à Open Library |

As duas primeiras opções correspondem às chaves `ConnectionStrings:Default` e `Mnemora:WebOrigin` em `appsettings`. Um exemplo sem segredos é:

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=mnemora.db"
  },
  "Mnemora": {
    "WebOrigin": "http://localhost:3000"
  }
}
```

Exemplo local no PowerShell, usando valores próprios:

```powershell
$env:ConnectionStrings__Default = "Data Source=mnemora.db"
$env:Mnemora__WebOrigin = "http://localhost:3000"
$env:ASPNETCORE_URLS = "http://localhost:5100"
```

O launch profile de desenvolvimento já fixa `http://localhost:5100`. Para usar outra porta, edite `src/Mnemora.Api/Properties/launchSettings.json` ou execute `dotnet run --no-launch-profile --project src/Mnemora.Api --urls http://localhost:5199`; no comando padrão, o perfil tem precedência sobre `ASPNETCORE_URLS`.

Para criar um Admin no seed, defina `ADMIN_EMAIL` e `ADMIN_PASSWORD` apenas no ambiente local. A senha precisa ter pelo menos 10 caracteres, uma letra maiúscula, uma minúscula e um dígito. Se o e-mail já pertencer a uma conta, o seed só atribui a role `Admin` quando a senha configurada corresponde à conta existente. Não grave credenciais, `.env.local` ou o banco SQLite no Git.

Em produção, publique frontend e `/api` sob a mesma origem e termine TLS no proxy. Configure em `Mnemora__KnownProxies__0` somente o IP de cada proxy confiável; assim a API reconhece o esquema HTTPS e o endereço real usado pelos rate limits sem aceitar headers encaminhados de fontes arbitrárias.

### Frontend

O valor padrão já atende ao desenvolvimento local. Para apontar o rewrite para outra API, crie `apps/web/.env.local`:

```dotenv
API_BASE_URL=http://localhost:5100
```

`API_BASE_URL` é lida no servidor Next.js. Ela não contém credenciais do usuário.

Para habilitar **Gerar pacote com IA** localmente com uma chave Groq, execute `.\scripts\configure-generation.ps1` na raiz do projeto e reinicie a API. O comando solicita a chave com entrada oculta e salva as opções em .NET user secrets, fora do Git; a configuração sobrevive à abertura de outro terminal. O painel informa o que falta e permite verificar novamente sem chamar o provedor. Em produção, configure as variáveis no host da API. Consulte o [guia de geração assistida](docs/assisted-generation.md) para configuração, limites e revisão.

## Banco, migrations e seed

Restaure a ferramenta e aplique as migrations versionadas a partir da raiz:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/Mnemora.Infrastructure --startup-project src/Mnemora.Api
```

Ao iniciar em `Development`, a API também executa `MigrateAsync`. No fluxo local documentado, o banco fica em `src/Mnemora.Api/mnemora.db` e é ignorado pelo Git.

O seed opcional cria, de forma idempotente, o pack original **O Arquivo da Neblina**. Se as duas variáveis de Admin estiverem preenchidas, também cria o usuário e atribui a role `Admin`; sem elas, apenas o pack é criado. O mesmo comando aplica migrations, executa o seed e mantém a API em execução:

```powershell
$env:ADMIN_EMAIL = "admin@example.test"
$env:ADMIN_PASSWORD = "Use-Uma-Senha-Local-Forte-2026"
dotnet run --project src/Mnemora.Api -- --seed
```

Use valores locais próprios. O repositório não fornece credenciais padrão.

## Executar em desenvolvimento

No primeiro terminal, na raiz:

```powershell
dotnet run --project src/Mnemora.Api
```

A API abre em `http://localhost:5100`. Em ambiente de desenvolvimento, Swagger UI fica disponível em `http://localhost:5100/swagger`.

Em outro terminal:

```powershell
Set-Location apps/web
npm run dev
```

Abra `http://localhost:3000`. Cadastre um leitor pela interface; para acessar `/admin`, use um usuário provisionado pelo seed com as variáveis de Admin.

## Publicar no Render

O repositório inclui um [`render.yaml`](render.yaml) para publicar frontend e API juntos em um único Web Service Docker. O Blueprint padrão cria uma **demonstração gratuita**, sem cartão e sem Admin inicial, na região Virginia. O health check usa `/api/health`, e o deploy automático só ocorre depois que os checks do commit passam.

Abra o fluxo [Deploy to Render](https://render.com/deploy?repo=https://github.com/DiogoJP202/Mnemora), revise o Blueprint e aplique-o. A demonstração permite cadastrar leitores e experimentar o pack original, mas seu SQLite, contas, sessões, progresso e notas são efêmeros. O serviço entra em repouso após 15 minutos sem acessos, pode levar cerca de um minuto para voltar e pode perder todos os dados em repousos, reinícios ou novos deploys. Um aviso visível identifica esse ambiente na interface.

Para manter dados, contas e sessões entre reinícios, converta depois o serviço para `0.5c-512mb` e anexe um disco de 1 GB. Essa opção custa atualmente cerca de **US$ 7,25/mês**: US$ 7,00 pelo serviço e US$ 0,25 pelo disco. Consulte o [guia de implantação](docs/deployment.md) para o passo a passo, as limitações da demo e a configuração durável.

## Verificações

Execute a suíte .NET a partir da raiz:

```powershell
dotnet build Mnemora.slnx
dotnet test Mnemora.slnx
```

Valide o frontend:

```powershell
Set-Location apps/web
npm run lint
npm run build
```

Valide o manifesto, os tamanhos dos ícones e as regras de cache da PWA a partir da raiz:

```powershell
node --test tests/pwa/pwa-assets.test.mjs
```

Execute também o gate de segurança das dependências e seus testes, a partir da raiz:

```powershell
node scripts/audit-npm.mjs
node --test tests/security/audit-npm.test.mjs
```

O gate consulta a auditoria npm completa e bloqueia achados `high`/`critical`, com uma exceção temporária restrita à cadeia de desenvolvimento de `eslint-config-next` que usa `braces` 3.0.3 (`GHSA-vfj7-8cjw-p6xm`). Ela verifica advisory, versões, caminho e classificação `dev-only`, e expira no início de 9 de novembro de 2026 (UTC). O ambiente de execução tem auditoria sem vulnerabilidades após a atualização de `sharp` para 0.35.5 e `source-map-js` para 1.2.2; o relatório completo ainda apresenta o aviso de desenvolvimento sem patch compatível. A justificativa, os limites e a remoção da exceção estão em [docs/dependency-security.md](docs/dependency-security.md).

O service worker só é registrado quando `NODE_ENV=production`. Para testar instalação e cache localmente, mantenha a API ativa e execute:

```powershell
Set-Location apps/web
npm run build
npm run start
```

Abra `http://localhost:3000` em um navegador compatível. A PWA guarda apenas a landing pública, ícones e assets estáticos do Next.js. Conteúdo autenticado continua dependente da rede e nunca entra no cache do service worker.

O workflow [`.github/workflows/ci.yml`](.github/workflows/ci.yml) repete em cada push para `main` e em cada pull request os testes .NET, auditoria NuGet, lint e build do frontend, testes da PWA e auditoria npm.

## Estrutura do repositório

```text
apps/web/                    Next.js App Router, interface e PWA
src/Mnemora.Api/             HTTP, auth, autorização e endpoints REST
src/Mnemora.Application/     política de visibilidade, DTOs e contratos
src/Mnemora.Domain/          entidades e enums do domínio
src/Mnemora.Infrastructure/  EF Core, Identity, consultas, seed e providers de livros
tests/Mnemora.Tests/         testes unitários e de integração da API
tests/pwa/                   testes do manifesto, ícones e service worker
tests/security/              testes do gate de auditoria npm
scripts/                     gate de segurança de dependências
docs/                        arquitetura, domínio, desenvolvimento e antisspoiler
```

## Endpoints principais

Todos os endpoints vivem sob `/api`. Rotas de escrita exigem `X-CSRF-TOKEN`; as marcadas como autenticadas usam a sessão do Identity.

| Área | Rotas principais |
| --- | --- |
| Saúde | `GET /api/health` |
| Autenticação | `GET /api/auth/csrf`, `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me` |
| Catálogo | `GET /api/books`, `GET /api/books/search?q=...`, `GET /api/books/{bookId}` |
| Biblioteca | `GET /api/library`, `POST/DELETE /api/library/{bookId}`, `POST /api/library/external`, `POST /api/library/manual`, `PATCH /api/library/{bookId}/progress`, `PATCH /api/library/{bookId}/status` |
| Livro autenticado | `GET /api/books/{bookId}/reading-units`, `GET /api/books/{bookId}/overview`, `POST /api/books/{bookId}/memory-pack-request` |
| Lore seguro | `GET /api/books/{bookId}/entities`, `/factions`, `/locations`, `/timeline`, `GET /api/entities/{entityId}` |
| Recall | `GET /api/books/{bookId}/recall?q=...` |
| Notas | `GET/POST /api/books/{bookId}/notes`, `PUT/DELETE /api/notes/{noteId}` |
| Revisão | `GET /api/books/{bookId}/review`, `POST /api/books/{bookId}/review/{entityId}` |
| Administração | `/api/admin/books`, workflow `/memory-pack/*` incluindo `GET /export`, `POST /validate` e `POST /import`, consulta/importação de `/reading-outline/*`, unidades, entidades, aliases, fatos, relações e previews seguros |
| Geração assistida (Admin) | `/api/admin/books/{bookId}/memory-pack/generations`: disponibilidade, separação das fontes, criação de sessão, progresso, avanço, resultado e cancelamento |
| Fontes automáticas (Admin) | `/api/admin/books/{bookId}/source-preparations`: índice, coleta por capítulo, cobertura, cancelamento e transferência para a geração |

O formulário do livro grava `status`, `currentReadingUnitId` e `currentPage` em uma única chamada a `PATCH /api/library/{bookId}/progress`. Campos ausentes preservam o valor atual; `null` explícito limpa unidade ou página, e todos os valores são validados antes de qualquer mutação. Um status explícito prevalece sobre a inferência automática de `Reading`. Selecionar “Nenhuma — limpar ponto de leitura” restaura o escopo sem progresso e revoga imediatamente o lore anteriormente visível; marcar `Finished` nunca concede conhecimento por conta própria.

`DELETE /api/library/{bookId}` exige sessão, antiforgery e rate limit. A operação transacional remove o vínculo, as notas privadas e as atividades de revisão somente do leitor atual. Livros curados ou importados permanecem no catálogo e dados de outras contas são preservados. Um livro manual privado do próprio leitor só é excluído do catálogo quando fica órfão, sem unidades, lore, relações ou referências de outra conta. A ausência do vínculo retorna `404`.

A busca externa usa a Open Library por padrão e não exige chave. `GOOGLE_BOOKS_API_KEY` habilita o Google Books como fonte adicional; falha em um provider não descarta resultados válidos do outro. Os resultados incluem, quando disponíveis, subtítulo, editora, data de publicação, idioma, ISBN-10/ISBN-13, número de páginas, categorias e identificação da edição. Capas são normalizadas para HTTPS. A Open Library usa dados de edição quando eles existem, sem fazer uma requisição adicional para cada resultado.

Um ISBN com espaços ou hífens é validado e normalizado antes da consulta; ISBN-10 e ISBN-13 equivalentes levam à mesma busca exata nos providers. A composição elimina duplicatas pelo ISBN canônico, preferindo ISBN-13, e, quando não há ISBN válido, pelo par provider/identificador. Somente os resultados externos, sem dados do usuário, ficam em cache na memória do processo por quatro horas. Respostas privadas continuam com `no-store` e fora do cache da PWA.

Na interface, um resultado externo abre uma confirmação somente leitura antes de qualquer escrita. O leitor confere edição, idioma, editora, data e ISBN e então confirma a inclusão. Só essa confirmação envia `{ "externalProvider": "...", "externalId": "..." }` a `POST /api/library/external`; o identificador deve ser exatamente o devolvido pela busca. O par provider/identificador torna a importação idempotente e permite que outros leitores reutilizem o mesmo registro bibliográfico, sem oferecer edição individual de um registro compartilhado. Quando uma edição está disponível, resultados da Open Library usam seu ID (`OL…M`) e conservam internamente o ID da obra (`OL…W`) para reconhecer importações feitas pela versão anterior da integração; sem uma edição válida, o ID da obra permanece como fallback.

A importação persiste somente os campos que já pertencem a `Book`: título, subtítulo, autor, capa, ISBN-10, ISBN-13, editora, data de publicação e idioma. Número de páginas, categorias e rótulo de edição ajudam na escolha, mas permanecem apenas na resposta de busca e na confirmação. Descrições e sinopses eventualmente devolvidas pelos providers são ignoradas e nunca chegam ao catálogo ou à interface, pois podem conter spoilers. A estante mostra subtítulo e um resumo da edição com editora, ano e idioma, usando ISBN como fallback; o detalhe mostra todos os metadados persistidos disponíveis.

Quando a busca não encontra o livro, `POST /api/library/manual` aceita `{ "title": "...", "author": "...", "isbn": "..." }`. Apenas `title` é obrigatório; autor e ISBN são opcionais. O ISBN, quando presente, aceita formatação comum, precisa ter dígito verificador válido e é armazenado na forma normalizada. O livro manual pertence ao leitor que o criou e não aparece para outras contas.

Cada DTO de livro separa `readingOutlineAvailable` de `memoryPackAvailable` e informa `memoryPackStatus` (`Unavailable`, `Requested`, `Preparing` ou `Available`) e `canRequestMemoryPack`. Todo livro aceita os quatro status de leitura, página de referência e notas privadas. Um índice publicado já permite escolher a unidade e registrar progresso, mesmo enquanto o lore está em preparação. Personagens, facções, lugares, timeline, Recall e revisão só ficam disponíveis quando o índice e o lore foram publicados e existe conteúdo editorial. Sem pack, o leitor pode solicitar a preparação de um livro compartilhado da sua biblioteca; existe no máximo um pedido pendente por usuário e livro, e repetições são idempotentes. O painel Admin prioriza livros com mais solicitações. Livros manuais privados não entram nessa fila.

O Admin prepara separadamente o índice e o conhecimento. Unidades e lore só podem ser alterados em rascunho; a publicação valida hierarquia, slugs, ordem e pontos de revelação. Despublicar o índice também recolhe o lore publicado, e publicar o lore limpa as solicitações pendentes daquele livro. Para edições exatas da Open Library (`OL…M`), o painel pode consultar o campo opcional `table_of_contents`, revisar a prévia e importar até 200 itens como unidades hierárquicas com rótulos neutros. Um sumário com mais de 200 itens válidos é rejeitado integralmente com `422`, sem truncamento nem criação de um índice parcial. A operação registra fonte e atribuição e é idempotente quando o rascunho existente tem os mesmos títulos. Nem a Open Library nem o Google Books fornecem personagens, relações, eventos ou pontos de revelação confiáveis; esse conhecimento continua sendo escrito e revisado por uma pessoa.

A migration `MemoryPackWorkflow` preserva como publicados os índices e o lore já existentes em bancos anteriores. Novas importações e cadastros manuais começam sem conteúdo editorial; livros criados pelo Admin começam em rascunho. O seed original é publicado explicitamente.

### Buscar e importar livros no Admin

Em `/admin/books`, use **Buscar livro para preparar** com título, autor ou ISBN. A busca reaproveita Open Library e Google Books quando configurado; não exige nova chave nem usa IA. Selecione **Revisar edição**, confira idioma, ISBN e demais metadados e confirme **Importar e abrir livro**. Um livro já cadastrado oferece **Abrir livro**. O cadastro manual fica como alternativa.

`GET /api/admin/books/search?q=...` pesquisa somente o catálogo compartilhado e os provedores configurados. `POST /api/admin/books/external` recebe apenas `externalProvider` e `externalId`; o servidor consulta os metadados e importa ou reutiliza o cadastro. Ambas exigem Admin, têm rate limit e respostas sem cache; a escrita exige antiforgery. Livros privados não são expostos. O serviço de importação é compartilhado com o leitor, preenche somente campos ausentes e preserva identidade, conteúdo editorial, publicação e progresso existentes. A importação administrativa não cria associação na biblioteca pessoal, pedido de pack, capítulos ou lore.

Depois da importação, os controles existentes permitem consultar sumário, preparar fontes, gerar ou importar um pacote em rascunho e revisá-lo antes de publicar. Veja o [plano das próximas fases](docs/automated-preparation-plan.md) e o [levantamento dos três primeiros livros de Harry Potter](docs/harry-potter-source-assessment.md).

### Descobrir referências públicas

No detalhe administrativo, use **Encontrar referências públicas → Descobrir fontes públicas**. O servidor confere a edição exata na Open Library, procura até três candidatos no Wikidata, descarta tipos não literários e apresenta links da Wikipedia em português e inglês quando disponíveis. O painel distingue ISBN, vínculo da obra e correspondência não confirmada; mostra sumário candidato, lacunas e procedência e permite baixar o levantamento. Não exige chave adicional nem chama IA.

Referências e sinopses gerais podem conter spoilers; essa descoberta bibliográfica não as envia ao gerador nem as transforma em lore. A API é `POST /api/admin/books/{id}/source-discovery`, protegida por Admin/CSRF, rate limit, HTTP limitado e respostas sem cache privado. A fase 25 acrescenta um caminho separado de [coleta de sinopses por capítulo](docs/automatic-sources.md), inicialmente para os três primeiros Harry Potter no Wikibooks.

### Importar um pacote editorial completo

O Admin pode baixar um modelo JSON ligado à edição, preenchê-lo com unidades, entidades, aliases, fatos e relações e validar o arquivo antes de importar. A gravação repete a validação e cria um rascunho atômico em um livro compartilhado sem conteúdo editorial. Ela não modifica metadados, progresso, notas ou histórico de leitores, e não publica automaticamente. Um pacote idêntico pode ser reenviado sem recriar IDs; conteúdo existente diferente retorna conflito e precisa ser editado pelos controles de autoria.

Confira o [formato v1 e seu fluxo](docs/memory-pack-format.md), os limites e o [exemplo original As Cartas do Farol](docs/examples/memory-pack-v1.json). Depois da importação, revise a prévia em vários pontos de leitura e publique índice e lore separadamente. A exportação contém conhecimento completo para o Admin; as telas do leitor continuam recebendo somente conteúdo já revelado.

### Gerar um pacote com IA

No detalhe administrativo de um **livro compartilhado sem conteúdo editorial e sem publicação**, use **Gerar pacote com IA → Buscar e preparar fontes**. Para Pedra Filosofal, Câmara Secreta e Prisioneiro de Azkaban, o backend obtém o índice e as seções de sinopse do guia comunitário Wikibooks, em português com alternativa inglesa por capítulo quando necessária. Confira a correspondência com a edição e então use **Gerar pacote com as fontes coletadas**. Você não precisa escrever resumos ou preparar um JSON para esse caminho. A entrada manual permanece em **Alternativa: fornecer resumos por capítulo manualmente**, para outras obras e exceções.

A geração processa cada capítulo em ordem e entrega o JSON v1 com um relatório separado de evidências e fontes/revisões. Revise candidatos, citações e revelações antes de enviar à validação e importação atuais; o pacote sempre entra em rascunho. Capítulos sem sinopse e índice não confirmado tornam o resultado parcial e exigem reconhecimento explícito. Análises, páginas de personagens e conhecimento próprio do modelo não são usados para preencher lacunas.

O recurso permanece **desabilitado por padrão**. Para habilitar, configure exclusivamente no backend `MemoryPackGeneration__Enabled=true`, `MemoryPackGeneration__Provider=Groq`, `MemoryPackGeneration__Model=openai/gpt-oss-120b` e `MemoryPackGeneration__ApiKey` como segredo. Confirme a organização no Groq Free antes de ativar: o aplicativo não verifica faturamento nem oferece fallback pago. Não coloque a chave no frontend ou no Git. A API .NET não lê arquivos `.env` automaticamente.

As fontes são enviadas ao Groq. Sessões ficam somente na memória do backend e desaparecem em reinícios ou expiração; baixe os dois artefatos durante a revisão. Uma unidade já cadastrada, mesmo em rascunho, impede a geração neste MVP. Leia o [guia de configuração, uso e limites](docs/assisted-generation.md) e use as [fontes fictícias originais](docs/examples/generation-sources.txt) para uma avaliação controlada.

## Limitações do MVP

- recuperação de senha ainda apresenta apenas uma orientação na interface;
- livros importados permitem organizar status, página e notas e podem receber um índice da Open Library quando a edição oferece sumário, mas o lore continua dependendo de curadoria e publicação administrativas;
- livros cadastrados manualmente são privados, não entram na fila de solicitações e não ganham um pack automaticamente;
- a busca do leitor é textual; busca semântica e respostas por IA ao leitor não fazem parte desta versão;
- a revisão registra “Eu lembro” ou “Preciso revisar”, sem agendamento por repetição espaçada;
- SQLite atende ao uso local e a uma implantação simples, sem a estratégia operacional de um banco multi-instância;
- o modo offline cobre somente o shell público e assets estáticos, não biblioteca, notas, Admin ou outras respostas privadas;
- cada usuário pode manter até 200 notas por livro, e o histórico de revisão conserva as 500 atividades mais recentes por usuário e livro;
- o conteúdo de lore e seus pontos de revelação são curados pelo Admin, com autoria manual, geração sobre sinopses coletadas ou fornecidas e importação de um pacote revisado; o formato v1 não substitui nem mescla conteúdo existente diferente;
- a coleta pública inicial é restrita aos três primeiros Harry Potter e às seções narrativas permitidas do Wikibooks. Não há pesquisa web genérica, leitura de PDF/EPUB ou garantia semântica de veracidade/ausência de spoilers; sessões temporárias e quotas podem interromper livros longos.

As evoluções candidatas estão registradas em [future.md](future.md).

## Documentação

- [Plano de implementação](docs/implementation-plan.md)
- [Arquitetura](docs/architecture.md)
- [Modelo de domínio](docs/domain-model.md)
- [Motor antisspoiler](docs/spoiler-system.md)
- [Guia de desenvolvimento](docs/development.md)
- [Formato de pacote editorial JSON v1](docs/memory-pack-format.md)
- [Geração assistida: operação e limites](docs/assisted-generation.md)
- [Próximas fases: adição e preparação automatizadas](docs/automated-preparation-plan.md)
- [Cobertura inicial: três primeiros livros de Harry Potter](docs/harry-potter-source-assessment.md)
- [Descoberta de fontes públicas: operação e limites](docs/source-discovery.md)
- [Segurança de dependências](docs/dependency-security.md)
- [Implantação no Render](docs/deployment.md)
- [Arquitetura técnica](docs/technical-architecture.md)
- [Relatório final](docs/final-report.md)
