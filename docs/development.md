# Desenvolvimento do Mnemora

Requisitos: Node.js 24 LTS, npm, .NET SDK 10 e Git. Os comandos abaixo são executados na raiz do repositório, exceto quando indicado. O SQLite local fica em `src/Mnemora.Api/mnemora.db` ao rodar a API pelo projeto e é ignorado pelo Git.

```powershell
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/Mnemora.Infrastructure --startup-project src/Mnemora.Api
dotnet run --project src/Mnemora.Api
```

Em outro terminal:

```powershell
Set-Location apps/web
npm ci
npm run dev
```

O frontend abre em `http://localhost:3000` e encaminha `/api/*` para `http://localhost:5100`. Para mudar a porta, configure `API_BASE_URL` em `apps/web/.env.local` e `ASPNETCORE_URLS`/launch profile no backend. O arquivo `.env.example` enumera as variáveis; ele não é carregado automaticamente pelo PowerShell ou .NET.

Para criar o pack demonstrativo e um Admin local, configure `ADMIN_EMAIL` e `ADMIN_PASSWORD` como variáveis de processo no terminal da API e rode `dotnet run --project src/Mnemora.Api -- --seed`. A senha deve atender às regras do Identity. O seed é idempotente: rodá-lo novamente não duplica o livro. Sem essas duas variáveis, o livro de demonstração ainda é criado, mas nenhum Admin é provisionado. Não versionar credenciais, bancos locais ou `.env.local`.

Os endpoints administrativos usam `/api/admin` e exigem a role Admin. Cada escrita pela interface obtém um token em `GET /api/auth/csrf` e o envia no header `X-CSRF-TOKEN`. O backend devolve 401 sem sessão e 403 para leitor sem role Admin.

Na jornada do leitor, o catálogo local está em `GET /api/books` e a busca em `GET /api/books/search?q=...`. A busca externa pelo Google Books só é habilitada com `GOOGLE_BOOKS_API_KEY`; sem chave, a busca local continua funcionando. A importação de um resultado externo ocorre em `POST /api/library/external` e é idempotente por identificador do provider. O Mnemora importa apenas metadados bibliográficos: a descrição externa não entra no catálogo, pois pode revelar partes da história.

A biblioteca pertence à sessão autenticada (`GET /api/library`). O leitor adiciona um livro com `POST /api/library/{bookId}`, escolhe uma unidade com `PATCH /api/library/{bookId}/progress` e pode retroceder a qualquer momento. A página opcional é uma referência pessoal, sem efeito sobre a visibilidade do lore. Em `GET /api/books/{bookId}/reading-units`, títulos de unidades ainda não alcançadas são substituídos por rótulos neutros. O status “Finished” também não substitui uma unidade de progresso válida para o motor antisspoiler.

Para explorar o pack de um livro da biblioteca, use `GET /api/books/{bookId}/entities` (opcional `type`), `/factions`, `/locations` e `/timeline`; o detalhe fica em `GET /api/entities/{entityId}`. Todos esses resultados passam pelo `KnowledgeReader` e são calculados novamente após cada alteração de progresso. Uma entidade futura responde 404 mesmo quando seu identificador é conhecido por outro meio.

O Recall usa `GET /api/books/{bookId}/recall?q=...`. Termos vazios devolvem uma lista vazia e termos acima de 100 caracteres são rejeitados. A consulta ignora diferenças de caixa e diacríticos, procura somente em entidades, resumos, aliases, fatos e pistas já visíveis, retorna no máximo 12 resumos seguros e não indica se um termo existe em conteúdo futuro.

Notas privadas usam `GET/POST /api/books/{bookId}/notes` e `PUT/DELETE /api/notes/{noteId}`. O conteúdo deve ter de 1 a 4.000 caracteres. Notas gerais permanecem disponíveis mesmo sem um ponto de leitura; notas ligadas a entidade ou unidade só aparecem enquanto essa referência estiver dentro do progresso atual. O parâmetro opcional `entityId` filtra a lista para a tela de uma entidade. Todos os acessos usam o usuário da sessão e IDs alheios respondem 404.

A revisão usa `GET /api/books/{bookId}/review` para selecionar, de forma determinística, até cinco entidades conhecidas. `POST /api/books/{bookId}/review/{entityId}` com `{ "remembered": true|false }` registra “Eu lembro” ou “Preciso revisar”. O registro não libera informação e uma entidade fora do ponto de leitura responde 404.

Validações usuais:

```powershell
dotnet build Mnemora.slnx
dotnet test Mnemora.slnx
Set-Location apps/web
npm run lint
npm run build
```
