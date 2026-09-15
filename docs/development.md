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

Validações usuais:

```powershell
dotnet build Mnemora.slnx
dotnet test Mnemora.slnx
Set-Location apps/web
npm run lint
npm run build
```
