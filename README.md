# Mnemora

> **Remember the story. Not the spoilers.**

Mnemora é um companion de leitura mobile-first para recordar personagens, lugares, facções, relações e acontecimentos sem ultrapassar o ponto atual do leitor. A aplicação também oferece biblioteca pessoal, progresso por unidade de leitura, notas privadas, revisão curta de entidades conhecidas, catálogo e uma área administrativa para criar packs de lore.

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
| Web | Next.js 16.3.5, React 19.3.0, TypeScript 5.9.3, Tailwind CSS 4.3.3 |
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
| `GOOGLE_BOOKS_API_KEY` | vazio | Habilita busca e importação pelo Google Books |

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

O repositório inclui um [`render.yaml`](render.yaml) para publicar frontend e API juntos em um único Web Service Docker. O Blueprint usa a região Virginia, o plano `0.5c-512mb`, health check em `/api/health` e um disco persistente de 1 GB para o SQLite e as chaves de sessão.

Abra o fluxo [Deploy to Render](https://render.com/deploy?repo=https://github.com/DiogoJP202/Mnemora), revise o Blueprint e informe `ADMIN_EMAIL` e `ADMIN_PASSWORD` quando solicitado. Esses segredos usam `sync: false` e não ficam no Git. O deploy automático ocorre após os checks do commit passarem.

A configuração custa atualmente **US$ 7,25/mês**: US$ 7,00 pelo serviço e US$ 0,25 pelo disco de 1 GB. Ela opera com uma única instância por causa do SQLite; consulte o [guia de implantação](docs/deployment.md) para o passo a passo, validação, backups e caminho de evolução para múltiplas instâncias.

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
src/Mnemora.Infrastructure/  EF Core, Identity, consultas, seed e Google Books
tests/Mnemora.Tests/         testes unitários e de integração da API
tests/pwa/                   testes do manifesto, ícones e service worker
docs/                        arquitetura, domínio, desenvolvimento e antisspoiler
```

## Endpoints principais

Todos os endpoints vivem sob `/api`. Rotas de escrita exigem `X-CSRF-TOKEN`; as marcadas como autenticadas usam a sessão do Identity.

| Área | Rotas principais |
| --- | --- |
| Saúde | `GET /api/health` |
| Autenticação | `GET /api/auth/csrf`, `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me` |
| Catálogo | `GET /api/books`, `GET /api/books/search?q=...`, `GET /api/books/{bookId}` |
| Biblioteca | `GET /api/library`, `POST/DELETE /api/library/{bookId}`, `POST /api/library/external`, `PATCH /api/library/{bookId}/progress`, `PATCH /api/library/{bookId}/status` |
| Livro autenticado | `GET /api/books/{bookId}/reading-units`, `GET /api/books/{bookId}/overview` |
| Lore seguro | `GET /api/books/{bookId}/entities`, `/factions`, `/locations`, `/timeline`, `GET /api/entities/{entityId}` |
| Recall | `GET /api/books/{bookId}/recall?q=...` |
| Notas | `GET/POST /api/books/{bookId}/notes`, `PUT/DELETE /api/notes/{noteId}` |
| Revisão | `GET /api/books/{bookId}/review`, `POST /api/books/{bookId}/review/{entityId}` |
| Administração | `/api/admin/books`, `/api/admin/books/{bookId}/reading-units`, `/api/admin/books/{bookId}/entities`, `/api/admin/entities/{entityId}`, aliases, fatos, relações e previews seguros |

A busca externa requer `GOOGLE_BOOKS_API_KEY`. A importação é idempotente pelo identificador do Google Books e descarta descrições externas, pois elas podem conter spoilers.

## Limitações do MVP

- recuperação de senha ainda apresenta apenas uma orientação na interface;
- livros importados recebem metadados bibliográficos, mas não ganham um pack de lore automaticamente;
- a busca é textual; busca semântica e recursos de IA não fazem parte desta versão;
- a revisão registra “Eu lembro” ou “Preciso revisar”, sem agendamento por repetição espaçada;
- SQLite atende ao uso local e a uma implantação simples, sem a estratégia operacional de um banco multi-instância;
- o modo offline cobre somente o shell público e assets estáticos, não biblioteca, notas, Admin ou outras respostas privadas;
- cada usuário pode manter até 200 notas por livro, e o histórico de revisão conserva as 500 atividades mais recentes por usuário e livro;
- o conteúdo de lore e seus pontos de revelação são curados manualmente pelo Admin.

As evoluções candidatas estão registradas em [future.md](future.md).

## Documentação

- [Plano de implementação](docs/implementation-plan.md)
- [Arquitetura](docs/architecture.md)
- [Modelo de domínio](docs/domain-model.md)
- [Motor antisspoiler](docs/spoiler-system.md)
- [Guia de desenvolvimento](docs/development.md)
- [Implantação no Render](docs/deployment.md)
- [Arquitetura técnica](docs/technical-architecture.md)
- [Relatório final](docs/final-report.md)
