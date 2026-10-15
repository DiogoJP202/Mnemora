# Arquitetura técnica do Mnemora

## Objetivo e princípios

O Mnemora é um companion de leitura mobile-first que ajuda o leitor a recuperar personagens, lugares, facções, relações e eventos sem revelar conteúdo posterior ao seu progresso. O requisito estrutural é que informação futura não chegue ao navegador. Por isso, o backend determina o conjunto conhecido e filtra as linhas antes de montar qualquer DTO público.

Os princípios que orientam a implementação são:

- o progresso canônico é uma unidade de leitura ordenada; a página é apenas uma referência pessoal;
- a fronteira de revelação é inclusiva: algo revelado na ordem atual já pode aparecer;
- ausência de progresso produz um conjunto de lore vazio;
- retroceder o progresso revoga conteúdo que deixou de ser conhecido;
- busca, detalhe, timeline, revisão, notas vinculadas e preview usam a mesma regra de visibilidade;
- dados privados são obtidos sob demanda e não entram em cache compartilhado ou no cache da PWA;
- conteúdo administrativo e metadados externos só se tornam lore por uma decisão editorial explícita.

## Visão do sistema

```mermaid
flowchart LR
    Browser[Navegador / PWA] -->|origem do frontend| Next[Next.js 16 App Router]
    Next -->|rewrite de /api/*| Api[ASP.NET Core 10]
    Api --> App[Application]
    Api --> Infra[Infrastructure]
    App --> Domain[Domain]
    Infra --> App
    Infra --> Domain
    Infra --> Db[(SQLite / EF Core 10)]
    Infra -. padrão sem chave .-> OpenLibrary[Open Library]
    Infra -. chave opcional .-> Google[Google Books]
    Infra -. geração editorial opcional .-> Groq[Groq GPT-OSS 120B]
```

O produto é um monólito modular com dois processos no desenvolvimento local. O Next.js atende `http://localhost:3000`; o ASP.NET Core atende `http://localhost:5100`. O rewrite de `/api/:path*` mantém as chamadas do navegador na origem do frontend, inclusive cookies e antiforgery. Em uma implantação, o mesmo contrato exige que o proxy preserve `/api` na mesma origem pública.

Não há cache distribuído nem fila durável. O estado persistente fica no SQLite. A Open Library é o provider externo padrão e funciona sem chave; o Google Books é uma fonte adicional quando `GOOGLE_BOOKS_API_KEY` está configurada. Ambos fornecem metadados bibliográficos; uma edição exata da Open Library também pode oferecer um sumário opcional para revisão administrativa. Um `IMemoryCache` local guarda por quatro horas apenas respostas públicas de pesquisa dos providers; dados de usuário continuam fora de qualquer cache compartilhado ou da PWA. O serviço opcional de geração editorial usa Groq e armazenamento temporário próprio de sessões, sem persistir fontes no banco.

## Organização do repositório e dependências

| Área | Responsabilidade | Dependências relevantes |
| --- | --- | --- |
| `apps/web` | Landing, autenticação, biblioteca, leitura, lore, recall, notas, revisão, administração e PWA | Next.js 16.3.8, React 19.3, TypeScript |
| `src/Mnemora.Domain` | Entidades, enums e estado persistente do domínio | Nenhuma outra camada do produto |
| `src/Mnemora.Application` | Regras compartilhadas de visibilidade, integridade e contratos de projeção | Domain |
| `src/Mnemora.Infrastructure` | EF Core, Identity, SQLite, consultas seguras, consistência, seed e providers Open Library/Google Books | Application e Domain |
| `src/Mnemora.Api` | Composição, autenticação, autorização, validação HTTP, endpoints e políticas transversais | Application e Infrastructure |
| `tests/Mnemora.Tests` | Testes de domínio, integração e endpoints com `WebApplicationFactory` | Todas as camadas do backend |
| `tests/pwa` | Contratos do manifesto, ícones e cache do service worker | Node test runner |

O `MnemoraDbContext` também é o store do ASP.NET Core Identity. As migrations ficam em `src/Mnemora.Infrastructure/Persistence/Migrations`; o banco local `mnemora.db` é ignorado pelo Git.

## Modelo de dados

```mermaid
erDiagram
    Book ||--o{ ReadingUnit : organiza
    Book ||--o{ UserBook : biblioteca
    Book ||--o{ LoreEntity : contem
    ReadingUnit ||--o{ ReadingUnit : hierarquia
    ReadingUnit ||--o{ LoreEntity : primeira_revelacao
    LoreEntity ||--o{ EntityAlias : aliases
    LoreEntity ||--o{ LoreFact : fatos
    LoreEntity ||--o{ LoreRelation : origem
    LoreEntity ||--o{ LoreRelation : destino
    UserBook }o--o| ReadingUnit : progresso
    Book ||--o{ UserNote : notas
    Book ||--o{ MemoryPackRequest : solicitações
    LoreEntity ||--o{ UserRecallActivity : revisoes
    IdentityUser ||--o{ MemoryPackRequest : solicita
```

- `Book` guarda título, subtítulo, autoria, capa, ISBN-10/ISBN-13, editora, data de publicação, idioma e identificadores externos. `CatalogKind` distingue `Curated`, `Imported` e `Private`; somente o tipo privado possui `OwnerUserId`. A combinação de provider e identificador externo é única, e ISBNs normalizados de livros privados são únicos por proprietário. Página total, categorias e rótulo de edição são dados transitórios da busca, sem novas colunas persistentes.
- `ReadingUnit` representa parte, capítulo, seção ou subseção. `OrderIndex` é absoluto e único dentro do livro; `ParentUnitId` cria a hierarquia. `SafeLabel` pode ser mostrado antes da unidade, enquanto `Title` só é exposto depois da fronteira.
- `UserBook` registra posse na biblioteca, status, unidade atual, página informativa e timestamps do ciclo de leitura. A combinação de usuário e livro é única. Unidade e página podem ser limpas; ao remover o vínculo, notas e atividades de revisão daquele usuário também são eliminadas.
- `Book` mantém estados editoriais independentes para índice e lore (`None`, `Draft`, `Published`), instantes de publicação e procedência opcional do índice. Um rascunho persistido não implica disponibilidade pública.
- `MemoryPackRequest` registra demanda por livro compartilhado e é único por usuário/livro. A publicação do lore remove os pedidos já atendidos.
- `LoreEntity` representa personagem, facção, lugar, criatura, item, evento, organização ou conceito. `FirstKnownAtUnitId` libera nome, descrição curta e imagem. Eventos podem usar `ChronologyIndex`, independente da ordem de leitura.
- `EntityAlias`, `LoreFact` e `LoreRelation` têm seu próprio `RevealAtUnitId`. Uma relação referencia origem e destino e só pode existir a partir do momento em que ambos são conhecidos.
- `UserNote` pertence a um usuário e livro e pode apontar para uma entidade ou unidade.
- `UserRecallActivity` registra o feedback simples “Eu lembro” ou “Preciso revisar”; o MVP não calcula agendamento espaçado.

Além das FKs e índices do banco, `BookIntegrity` valida vínculos entre livros e ordem de revelação. `BookConsistency` impede que uma edição ou reordenação administrativa coloque aliases e fatos antes da entidade, relações antes dos participantes ou filhos antes do pai.

## Motor antisspoiler

`KnowledgeReader` é o caminho de leitura pública do lore. Cada requisição autenticada começa com `ScopeAsync`, que confirma a presença do livro na biblioteca, exige índice e lore publicados e converte `CurrentReadingUnitId` em `CurrentOrder`. Notas usam um escopo de biblioteca mais amplo para continuarem disponíveis sem pack, mas referências a unidade ou entidade ainda obedecem à disponibilidade editorial e ao progresso.

```mermaid
sequenceDiagram
    participant UI as Web
    participant API as API
    participant KR as KnowledgeReader
    participant DB as SQLite
    UI->>API: consulta autenticada
    API->>KR: ScopeAsync(userId, bookId)
    KR->>DB: UserBook + ordem da unidade atual
    API->>KR: entidade, busca, timeline ou revisão
    KR->>DB: SELECT com RevealOrder <= CurrentOrder
    DB-->>KR: somente linhas conhecidas
    KR-->>API: DTO seguro
    API-->>UI: resposta Cache-Control: no-store
```

As fronteiras são aplicadas assim:

| Conteúdo | Condição de visibilidade |
| --- | --- |
| Entidade | A ordem de `FirstKnownAtUnitId` é menor ou igual à ordem atual |
| Alias | O alias já foi revelado e sua entidade é conhecida |
| Fato e pista de memória | O fato já foi revelado e sua entidade é conhecida |
| Relação | A relação já foi revelada e origem e destino são conhecidos |
| Evento na timeline | A entidade do tipo evento é conhecida; a lista usa `ChronologyIndex` |
| Título de unidade | O título real aparece apenas quando a unidade é conhecida; antes disso, usa-se `SafeLabel` |
| Nota vinculada | Usuário é o dono e a entidade/unidade vinculada continua conhecida |

`KnownEntities`, `KnownAliases`, `KnownFacts` e `KnownRelations` retornam `IQueryable` com joins e predicados de ordem. A materialização ocorre depois desses filtros. O detalhe de uma entidade futura e um livro fora da biblioteca retornam `404`, sem confirmar a existência do conteúdo.

O Recall primeiro materializa apenas as fontes conhecidas e então normaliza caixa e diacríticos em .NET. Isso contorna as limitações Unicode de `LOWER`/`NOCASE` do SQLite sem ampliar o conjunto candidato. Um termo que existe somente em alias, fato ou pista futura não produz resultado. A busca vazia retorna uma lista vazia e o resultado é limitado a 12 entidades.

O preview administrativo constrói um `KnowledgeScope` para a unidade escolhida e chama o mesmo `KnowledgeReader`. Assim, o painel não mantém uma segunda interpretação da fronteira. Alterar ou retroceder o progresso também não depende de invalidação: a ordem é obtida novamente em cada requisição. Limpar `CurrentReadingUnitId` produz `CurrentOrder = null`; a próxima leitura retorna conjunto conhecido vazio e volta a projetar títulos de unidades como rótulos neutros.

## Segurança e privacidade

A autenticação usa ASP.NET Core Identity com cookie `Mnemora.Auth`. O cookie é `HttpOnly`, `SameSite=Lax` e se torna `Secure` em produção. E-mails são únicos; a senha exige pelo menos 10 caracteres, uma letra maiúscula, uma minúscula e um dígito; cinco falhas de login bloqueiam a conta por 15 minutos. Cadastro e login possuem rate limit por IP. Recall, integrações externas e escritas de notas/revisão também têm limites próprios.

Todas as mutações `POST`, `PUT`, `PATCH` e `DELETE` sob `/api` passam pela validação antiforgery. O cliente solicita `GET /api/auth/csrf` e envia o token no header `X-CSRF-TOKEN`; o cookie antiforgery também é `HttpOnly`, `SameSite=Lax` e `Secure` em produção. A API responde `401` e `403` diretamente, sem redirects HTML, e usa Problem Details para erros.

Os endpoints de biblioteca, lore, notas e revisão exigem autenticação. Consultas partem do identificador da sessão e filtram propriedade; IDs pertencentes a outro usuário são tratados como não encontrados. `/api/admin` requer a role `Admin`. Credenciais administrativas não ficam no repositório: o seed lê `ADMIN_EMAIL` e `ADMIN_PASSWORD` do ambiente. Se o e-mail já existir, a promoção só ocorre quando a senha configurada corresponde à conta; resultados de criação da role e atribuição também são verificados.

As mutações de acompanhamento usam o rate limit `memory-write`. A remoção da estante ocorre em transação e apaga somente `UserBook`, `UserNote` e `UserRecallActivity` do usuário autenticado. Registros `Curated` e `Imported` e as memórias de outras contas permanecem. Um livro `Private` só é removido quando pertence ao leitor, não possui unidades ou lore e não tem outra referência.

Um middleware aplica `Cache-Control: no-store` a toda resposta sob `/api`. O cliente também usa `fetch` com `cache: "no-store"` e `credentials: "same-origin"`. CORS permite apenas a origem configurada em `Mnemora:WebOrigin`. Os providers externos têm timeout; a composição consulta as fontes configuradas, combina resultados, tolera falha parcial e elimina duplicatas pelo ISBN canônico, preferindo ISBN-13, ou por provider/ID quando não há ISBN válido. ISBNs formatados são validados e convertidos para a forma canônica antes de uma consulta exata.

Resultados externos bem-sucedidos ficam no `IMemoryCache` por quatro horas, indexados somente pela consulta normalizada. A API continua executando a pesquisa local e a autorização do usuário em cada requisição; sessão, catálogo privado, biblioteca e progresso não entram nesse cache. A importação persiste somente título, subtítulo, autor, capa, ISBN-10/ISBN-13, editora, data de publicação e idioma. Página total, categorias e rótulo de edição são exibidos na seleção, mas não gravados. Descrições e sinopses eventualmente devolvidas pelos providers não entram no contrato de metadados, no DTO ou no banco, pois podem conter spoilers. `OPEN_LIBRARY_CONTACT` pode definir o contato do `User-Agent` enviado à Open Library; sem a variável, usa-se a URL pública do repositório. Para uma edição Open Library `OL…M`, um cliente separado pode consultar o `table_of_contents` e oferecer uma prévia ao Admin; somente os títulos e níveis explicitamente aprovados entram no rascunho do índice, com URL e atribuição. Cada usuário pode manter até 200 notas por livro, e a revisão conserva as 500 atividades mais recentes por usuário e livro. Essas escritas são serializadas no processo, de acordo com a implantação SQLite de instância única prevista para o MVP.

O catálogo público (`GET /api/books`) contém apenas livros curados. A busca local inclui livros curados, importados compartilháveis e os cadastros privados do usuário atual. O detalhe e a inclusão na biblioteca tratam o livro privado de outro usuário como inexistente. Livros importados são reutilizados pelo par provider/ID; a Open Library também devolve o Work ID como identidade de compatibilidade, para que um novo resultado `OL…M` reconheça registros `OL…W` importados antes da seleção por edição. Livros manuais são deduplicados somente dentro da conta proprietária. O frontend não permite alterações individuais nos metadados compartilhados: ele mostra uma confirmação somente leitura e envia o provider/ID escolhido apenas depois da ação explícita do leitor.

O Next.js envia Content Security Policy, `X-Content-Type-Options`, `X-Frame-Options`, política de referrer, Permissions Policy e HSTS. Em produção, o proxy deve terminar TLS, preservar a mesma origem pública e ser cadastrado explicitamente em `Mnemora:KnownProxies`; a API só então usa `X-Forwarded-For` e `X-Forwarded-Proto` para esquema, cookies e partições de rate limit.

## Fluxos funcionais

### Leitor

1. O usuário se cadastra ou entra e recebe a sessão HttpOnly.
2. A busca consulta o catálogo local e a Open Library; se houver `GOOGLE_BOOKS_API_KEY`, também consulta o Google Books. Um ISBN válido favorece a consulta exata, e resultados de fontes diferentes são deduplicados sem comparar apenas título e autor.
3. O leitor abre a confirmação de um resultado externo, confere os dados da edição e confirma a inclusão. Só então o frontend chama `POST /api/library/external` com `{ "externalProvider": "...", "externalId": "..." }`; a operação reutiliza o livro importado quando ele já existe.
4. Se o livro não for encontrado, `POST /api/library/manual` aceita título obrigatório, autor opcional e ISBN opcional e cria um registro privado para o leitor.
5. O leitor grava status, unidade e página em uma única atualização validada. A página não muda o conhecimento; limpar a unidade revoga o conteúdo antes liberado.
6. Livros com índice publicado oferecem seleção da unidade e progresso; um memory pack completo também exige lore publicado e então oferece personagens, facções, lugares, timeline, detalhe e Recall projetados para a ordem atual.
7. Livros sem pack ainda oferecem os quatro status, página de referência e notas privadas. Em livros compartilhados, o leitor pode registrar uma solicitação idempotente de preparação.
8. A estante e o detalhe mostram os metadados bibliográficos persistidos daquela edição.
9. Notas gerais ficam disponíveis ao dono; notas vinculadas acompanham a visibilidade da referência.
10. A revisão seleciona deterministicamente até cinco entidades conhecidas e registra o feedback.
11. Uma confirmação acessível precede a remoção; ao confirmar, progresso, notas e histórico de revisão daquela conta são apagados.

### Administração

1. Um Admin criado por seed acompanha a fila ordenada por solicitações e coloca o livro em preparação.
2. O índice pode ser escrito manualmente ou importado, como rascunho, do sumário opcional de uma edição exata da Open Library.
3. O Admin publica o índice depois de validar hierarquia, ordem, slugs e rótulos; a partir daí o leitor pode registrar progresso por unidade.
4. O editor mantém entidades, aliases, fatos e relações em rascunho e associa cada item a uma unidade de revelação.
5. As operações recusam referências entre livros e sequências inconsistentes; endpoints filhos também rejeitam GUIDs de livros privados.
6. O preview simula o ponto escolhido com a projeção segura usada pelo leitor.
7. A publicação do lore libera o memory pack e encerra os pedidos pendentes. Para editar novamente, o Admin recolhe a etapa correspondente; recolher o índice também recolhe o lore.

O seed `O Arquivo da Neblina` contém conteúdo curto e original. Ele é idempotente por `ExternalProvider` e `ExternalId`; executar `--seed` novamente não duplica o pack.

## PWA, temas e cache

O App Router fornece metadata, manifesto em português, ícones próprios de 192 e 512 pixels, ícone maskable e tema light/dark/system. O service worker só é registrado em build de produção.

O cache `mnemora-shell-v1` possui uma allowlist estreita:

- pré-cache da landing pública e dos ícones locais;
- navegação para `/` em modo network-first, com a cópia pública como fallback;
- assets em `/_next/static/`, `/icons/` e `/favicon.ico` em modo cache-first;
- nenhuma interceptação de requisições RSC ou conteúdo fora desse shell.

Os prefixos `/api`, `/app` e `/admin` saem imediatamente do handler sem consulta, abertura ou gravação em Cache Storage. Login, recuperação e outros documentos também seguem direto para a rede. Respostas com `no-store` ou `private` não podem ser armazenadas, e o próprio `sw.js` é entregue com `no-cache, no-store, must-revalidate`.

## Operação, decisões e riscos

- O SQLite simplifica a instalação local e o MVP. Escrita concorrente intensa e múltiplas réplicas exigiriam a migração para um banco servidor.
- Uma implantação com múltiplos processos também deve substituir a serialização local das quotas por uma garantia transacional no banco ou um coordenador distribuído.
- Em desenvolvimento, a API aplica migrations ao iniciar. Em outros ambientes, a atualização deve ser executada explicitamente antes da aplicação, ou por um processo de release controlado.
- Cada proxy reverso confiável deve ter seu IP configurado em `Mnemora:KnownProxies`; headers encaminhados de proxies desconhecidos não devem participar das decisões de segurança.
- Livros importados e manuais não recebem lore automaticamente. Uma edição importada pode oferecer um sumário opcional para revisão administrativa, mas ele cria somente unidades em rascunho. Sem um memory pack publicado, a interface mantém status, página e notas e apresenta o estado editorial nas superfícies de personagens, lugares, timeline, Recall e revisão.
- O cache de pesquisas externas é local a cada processo e pode ser descartado em reinícios. Ele reduz chamadas repetidas, mas não coordena múltiplas instâncias.
- A remoção por livro limpa a memória pessoal associada àquela estante. Ela não substitui encerramento de conta, portabilidade nem uma política global de retenção de dados.
- A segurança antisspoiler depende da curadoria de `ShortDescription` e `ImageUrl` para o primeiro ponto de conhecimento e da marcação correta de cada revelação. As validações impedem inconsistência temporal estrutural, mas não avaliam semanticamente o texto escrito por um Admin.
- A PWA oferece o shell público offline. A área autenticada exige rede de propósito, para que progresso retrocedido e dados privados nunca sejam servidos de um cache local antigo.
- Recuperação de senha, busca semântica, respostas por IA ao leitor e repetição espaçada são extensões futuras. A [geração editorial assistida](assisted-generation.md) está disponível opcionalmente para livros compartilhados vazios, com revisão humana antes de importar e publicar.

## Geração editorial assistida

`AdminMemoryPackGenerationEndpoints` exige Admin e antiforgery nas escritas. `MemoryPackGenerationTarget` confere a identidade exportada pelo servidor e o alvo vazio antes de iniciar, avançar e entregar resultados. O serviço usa sessões do `MemoryPackGenerationSessionStore`, singleton limitado a uma sessão por Admin, duas globais e uma chamada de IA simultânea. A revisão e o cursor são do backend; `operationId` e `expectedRevision` protegem repetição e concorrência. Um livro preenchido ou publicado durante a geração passa a gerar conflito.

O `GroqMemoryPackGenerationProvider` envia somente a fonte corrente e entidades/aliases do prefixo conhecido ao endpoint fixo do modelo GPT-OSS 120B. Schema estrito limita a estrutura; `MemoryPackGenerationAssembler` confere evidências textuais e acrescenta candidatos sem reescrever apresentações. Documento v1 e relatório têm limites/hash independentes, sem uma consolidação global por IA. A política de leitura existente permanece intacta.

Nenhuma transação SQLite fica aberta durante a chamada ao provedor. Não há migration, job persistente ou publicação automática. A transferência existente valida novamente e importa em `Draft`. A API não garante que a conta do provedor seja gratuita; o operador confirma Groq Free antes da habilitação, sem fallback pago. Chave e opções ficam no backend. O Next.js encaminha as chamadas com timeout de proxy de 120 segundos. O [guia operacional](assisted-generation.md) detalha os contratos, limites, cancelamento, perda em reinício e testes sem API real.
