# Desenvolvimento do Mnemora

Requisitos: Node.js 24 LTS, npm, .NET SDK 10.0.400 (ou patch compatível aceito por `global.json`) e Git. Os comandos abaixo são executados na raiz do repositório, exceto quando indicado. O SQLite local fica em `src/Mnemora.Api/mnemora.db` ao rodar a API pelo projeto e é ignorado pelo Git.

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

O frontend abre em `http://localhost:3000` e encaminha `/api/*` para `http://localhost:5100`. Para mudar a porta, configure `API_BASE_URL` em `apps/web/.env.local` e edite o launch profile ou inicie o backend com `dotnet run --no-launch-profile --project src/Mnemora.Api --urls http://localhost:5199`. O launch profile fixa `5100` e tem precedência sobre `ASPNETCORE_URLS` no comando padrão. O arquivo `.env.example` enumera as variáveis; ele não é carregado automaticamente pelo PowerShell ou .NET.

Para criar o pack demonstrativo e um Admin local, configure `ADMIN_EMAIL` e `ADMIN_PASSWORD` como variáveis de processo no terminal da API e rode `dotnet run --project src/Mnemora.Api -- --seed`. A senha deve ter pelo menos 10 caracteres, uma letra maiúscula, uma minúscula e um dígito. O seed é idempotente: rodá-lo novamente não duplica o livro. Se o e-mail já existir, a role `Admin` só é atribuída quando a senha informada corresponde à conta. Sem essas duas variáveis, o livro de demonstração ainda é criado, mas nenhum Admin é provisionado. Não versionar credenciais, bancos locais ou `.env.local`.

Em produção, mantenha frontend e `/api` na mesma origem e termine TLS no proxy reverso. Cadastre somente os IPs dos proxies confiáveis em `Mnemora:KnownProxies`, por exemplo com `Mnemora__KnownProxies__0`; isso permite interpretar `X-Forwarded-For` e `X-Forwarded-Proto` sem confiar nesses headers quando vêm diretamente da internet.

Os endpoints administrativos usam `/api/admin` e exigem a role Admin. Cada escrita pela interface obtém um token em `GET /api/auth/csrf` e o envia no header `X-CSRF-TOKEN`. O backend devolve 401 sem sessão e 403 para leitor sem role Admin.

Na jornada do leitor, o catálogo curado está em `GET /api/books` e a busca por título, subtítulo, autor ou ISBN em `GET /api/books/search?q=...`. A busca combina o catálogo local com a Open Library, disponível sem chave. `GOOGLE_BOOKS_API_KEY` acrescenta o Google Books como fonte opcional. Os providers têm timeout independente, e uma falha parcial não elimina resultados obtidos pela outra fonte. `OPEN_LIBRARY_CONTACT` pode informar um e-mail ou URL no `User-Agent` das requisições; seu padrão é a URL pública do repositório.

Um ISBN formatado com espaços ou hífens passa pela validação compartilhada e é convertido para a forma canônica antes da consulta exata aos providers. ISBN-13 tem prioridade na deduplicação dos resultados, seguido por ISBN-10 e, na ausência de ISBN válido, pelo par provider/identificador externo. Consultas externas bem-sucedidas ficam por quatro horas no `IMemoryCache`; a chave contém apenas a consulta normalizada e o valor contém somente metadados públicos dos providers. Falhas não substituem o fallback entre fontes, e nenhum dado de sessão, biblioteca ou progresso entra nesse cache.

Os resultados externos podem trazer título, subtítulo, autor, capa HTTPS, ISBN-10, ISBN-13, editora, data de publicação, idioma, número de páginas, categorias e edição. A integração com a Open Library seleciona dados de edição dentro da própria resposta de busca, distinguindo-os dos dados gerais da obra sem introduzir chamadas por resultado. Google Books escolhe a melhor capa HTTPS disponível.

A interface exibe uma confirmação somente leitura antes da importação. O leitor pode conferir a edição, o idioma, a editora, a data e o ISBN, mas não altera um registro externo compartilhado. Somente ao confirmar a interface chama `POST /api/library/external` com o corpo abaixo. `externalProvider` e `externalId` devem ser os valores devolvidos pela busca, por exemplo `OpenLibrary` ou `GoogleBooks`; não construa o identificador manualmente. A operação é idempotente pelo par provider/identificador, e o registro importado pode ser reutilizado por outros leitores.

```json
{
  "externalProvider": "OpenLibrary",
  "externalId": "OL12345M"
}
```

A importação persiste somente título, subtítulo, autor, capa, ISBN-10, ISBN-13, editora, data de publicação e idioma, que já fazem parte do modelo `Book`. Número de páginas, categorias e identificação textual da edição são úteis para a escolha, mas não são persistidos. Descrições e sinopses eventualmente presentes na resposta de um provider são ignoradas: elas não entram no contrato da busca, na interface nem no banco, porque podem revelar partes da história. Quando um registro importado existente não possui um desses campos persistentes, uma nova inclusão pode preenchê-lo sem sobrescrever um valor já cadastrado.

Se o livro não estiver nos resultados, `POST /api/library/manual` aceita título obrigatório, autor opcional e ISBN-10 ou ISBN-13 opcional. A ausência de autor usa “Autor não informado”. Um ISBN informado pode conter espaços ou hífens, deve ter dígito verificador válido e é armazenado de forma normalizada; equivalentes ISBN-10 e ISBN-13 são tratados como a mesma edição. O livro manual é privado: somente a conta proprietária consegue encontrá-lo, abrir seus detalhes ou adicioná-lo à biblioteca.

```json
{
  "title": "Meu livro",
  "author": "Autora opcional",
  "isbn": "9780306406157"
}
```

A biblioteca pertence à sessão autenticada (`GET /api/library`). O leitor adiciona um livro com `POST /api/library/{bookId}`, escolhe uma unidade com `PATCH /api/library/{bookId}/progress` e pode retroceder a qualquer momento. A página opcional é uma referência pessoal, sem efeito sobre a visibilidade do lore. Em `GET /api/books/{bookId}/reading-units`, títulos de unidades ainda não alcançadas são substituídos por rótulos neutros. O status “Finished” também não substitui uma unidade de progresso válida para o motor antisspoiler.

Os DTOs de catálogo e biblioteca expõem `memoryPackAvailable`. Sem unidades de leitura, o livro continua aceitando mudança de status, página de referência e notas gerais; a interface não mostra navegação de lore, timeline, Recall ou revisão. Um provider externo ou cadastro manual nunca cria lore automaticamente.

Para explorar o pack de um livro da biblioteca, use `GET /api/books/{bookId}/entities` (opcional `type`), `/factions`, `/locations` e `/timeline`; o detalhe fica em `GET /api/entities/{entityId}`. Todos esses resultados passam pelo `KnowledgeReader` e são calculados novamente após cada alteração de progresso. Uma entidade futura responde 404 mesmo quando seu identificador é conhecido por outro meio.

O Recall usa `GET /api/books/{bookId}/recall?q=...`. Termos vazios devolvem uma lista vazia e termos acima de 100 caracteres são rejeitados. A consulta ignora diferenças de caixa e diacríticos, procura somente em entidades, resumos, aliases, fatos e pistas já visíveis, retorna no máximo 12 resumos seguros e não indica se um termo existe em conteúdo futuro.

Notas privadas usam `GET/POST /api/books/{bookId}/notes` e `PUT/DELETE /api/notes/{noteId}`. O conteúdo deve ter de 1 a 4.000 caracteres, e cada usuário pode manter até 200 notas por livro. Notas gerais permanecem disponíveis mesmo sem um ponto de leitura; notas ligadas a entidade ou unidade só aparecem enquanto essa referência estiver dentro do progresso atual. O parâmetro opcional `entityId` filtra a lista para a tela de uma entidade. Todos os acessos usam o usuário da sessão e IDs alheios respondem 404.

A revisão usa `GET /api/books/{bookId}/review` para selecionar, de forma determinística, até cinco entidades conhecidas. `POST /api/books/{bookId}/review/{entityId}` com `{ "remembered": true|false }` registra “Eu lembro” ou “Preciso revisar”. O registro não libera informação, uma entidade fora do ponto de leitura responde 404 e somente as 500 atividades mais recentes de cada combinação de usuário e livro são conservadas.

Validações usuais:

```powershell
dotnet build Mnemora.slnx
dotnet test Mnemora.slnx
node --test tests/pwa/pwa-assets.test.mjs
Set-Location apps/web
npm run lint
npm run build
```
