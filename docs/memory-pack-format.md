# Pacotes editoriais JSON — formato v1

A importação reúne unidades de leitura, personagens, facções, lugares, eventos, aliases, fatos e relações em um arquivo JSON. O arquivo é uma ferramenta do Admin para transportar conteúdo já escrito e revisado; metadados de livros não se tornam conhecimento automaticamente. A versão inicial importa um pacote em um livro compartilhado ainda sem conteúdo editorial. Ela nunca substitui um índice ou lore diferente já existente e nunca publica o conteúdo por conta própria.

O arquivo pode conter todas as revelações da história. Download, validação e importação exigem uma conta Admin. O leitor continua usando as projeções antisspoiler existentes, sem acesso ao arquivo completo, ao rascunho ou aos resultados de validação administrativos.

## Preparar e importar

1. Abra o livro na área Admin e confira título, autor, idioma e identificação da edição. O livro deve ser compartilhado; livros manuais privados do leitor não aceitam esta importação.
2. Baixe o modelo do livro. A exportação usa a referência bibliográfica existente e, quando não há conteúdo, devolve as coleções vazias para preenchimento. Mantenha o bloco `book` ligado à edição de destino.
3. Edite o JSON em UTF-8, preenchendo unidades, entidades, aliases, fatos e relações. Use slugs para as referências internas, sem copiar GUIDs de outro banco. Mantenha rótulos neutros para unidades futuras e atribua a cada revelação uma unidade correta.
4. Selecione o arquivo no painel e valide. A validação mostra contagens e problemas sem gravar dados; corrija todos os problemas e confira novamente a edição. Um pacote válido estruturalmente ainda precisa de revisão humana do texto e dos pontos de revelação.
5. Importe o rascunho. O backend repete a validação e grava o conjunto de uma vez. Uma falha não deixa unidades ou conhecimento parcialmente importados.
6. Use a prévia editorial existente em vários pontos de leitura. Publique o índice e o lore pelos controles separados de publicação, depois da revisão. O índice pode ser publicado antes do conhecimento.

Ao baixar um pacote já preenchido, o arquivo representa o conteúdo editorial atual, com referências por slug. Ele não contém biblioteca, progresso, notas ou atividades de revisão de leitores. A exportação não é uma cópia completa do banco nem um mecanismo de backup dos dados pessoais.

## Estrutura do documento

Os nomes de campos usam camelCase. `schemaVersion` deve ser o número `1`. Campos desconhecidos são recusados em vez de ignorados, para detectar erros de escrita e incompatibilidades. Não inclua IDs de banco, estados de publicação ou dados de leitores. Valores opcionais podem ser omitidos; use as coleções `readingUnits`, `entities`, `relations`, `aliases` e `facts` mesmo quando estiverem vazias. Um modelo vazio pode ser exportado, mas a importação exige ao menos uma unidade e uma entidade.

| Bloco | Campos |
| --- | --- |
| Raiz | `schemaVersion`, `book`, `source` opcional, `readingUnits`, `entities`, `relations` |
| `book` | `title`, `author`; `externalProvider`, `externalId`, `isbn10`, `isbn13`, `language` opcionais |
| `source` | `url` e `attribution` opcionais |
| Unidade | `slug`, `title`, `safeLabel`, `type`, `orderIndex`; `parentSlug` e `description` opcionais |
| Entidade | `slug`, `type`, `name`, `firstKnownAtUnitSlug`, `importance`, `aliases`, `facts`; `shortDescription`, `imageUrl`, `chronologyIndex` opcionais |
| Alias | `alias`, `revealAtUnitSlug` |
| Fato | `content`, `type`, `importance`, `revealAtUnitSlug`; `memoryHint` opcional |
| Relação | `sourceEntitySlug`, `targetEntitySlug`, `relationType`, `revealAtUnitSlug`; `label` opcional |

O bloco `book` é uma referência para conferir o destino. Todos os seus campos precisam corresponder exatamente ao destino, incluindo maiúsculas e minúsculas, depois de remover espaços nas bordas. Um campo opcional ausente, `null` ou vazio representa ausência de valor; omitir `language`, por exemplo, não corresponde a um livro que já tem idioma cadastrado. Por isso, comece sempre pelo modelo exportado do livro.

Importar não modifica título, autor, capa, ISBN, idioma, provider ou qualquer outro metadado do catálogo. Não use o formato para adaptar um pacote a outra edição apenas trocando seu identificador: capítulos e pontos de revelação podem ter posições diferentes. A compatibilidade com a edição de destino é obrigatória e conferida novamente no servidor.

`source.url` e `imageUrl`, quando informados, devem ser URLs HTTPS absolutas, sem credenciais embutidas. A fonte é procedência editorial, não uma instrução para buscar ou gerar conteúdo. O arquivo deve ter conteúdo próprio, autorizado ou permitido por sua fonte; registre uma atribuição adequada. O Mnemora não consulta a URL de procedência durante a importação. `source` omitido, `null` ou com os dois campos vazios representa ausência de procedência.

### Unidades de leitura

- `type` aceita `Part`, `Chapter`, `Section` ou `Subsection`.
- `orderIndex` é uma ordem absoluta positiva, única no livro; valores como 10, 20 e 30 deixam espaço para inserir unidades intermediárias.
- `slug` identifica a unidade dentro do pacote e usa letras minúsculas sem acento, dígitos e hífens. `parentSlug` aponta para outra unidade do mesmo arquivo. O pai precisa vir antes do filho na ordem de leitura, e a hierarquia não pode conter ciclos.
- `title` pode conter uma revelação, enquanto `safeLabel` deve permanecer neutro, como “Capítulo 3”. A neutralidade do texto exige revisão humana.
- `description` é editorial; a existência do campo não autoriza mostrar uma descrição futura ao leitor.

### Conhecimento e revelações

`type` da entidade aceita `Character`, `Faction`, `Location`, `Creature`, `Item`, `Event`, `Organization`, `Concept` ou `Other`. `firstKnownAtUnitSlug` aponta para a unidade na qual a entidade se torna conhecida. Nome, resumo e imagem devem ser seguros já nesse primeiro ponto. Uma identidade futura deve entrar como alias com sua própria revelação, em vez de aparecer antecipadamente no nome ou no resumo.

`type` do fato aceita `Description`, `Title`, `Background`, `Event`, `RelationshipContext` ou `Other`. Alias e fato não podem ser revelados antes da entidade. `memoryHint` também é conteúdo sujeito ao ponto de revelação do fato; não escreva uma pista que antecipe um fato futuro.

Uma relação aponta para duas entidades do arquivo e não pode ser revelada antes de nenhuma delas. `relationType` é um texto editorial curto, por exemplo `ColaboraCom`; não é um enum fixo. `label`, quando presente, precisa ser seguro na unidade da relação.

`chronologyIndex` pode ordenar eventos na timeline, mas não concede visibilidade. A liberação depende de `firstKnownAtUnitSlug`, e a página atual do leitor continua apenas informativa. Avançar e retroceder usa a mesma política antisspoiler aplicada ao conteúdo cadastrado manualmente.

`importance` é um inteiro maior ou igual a zero, tanto na entidade quanto no fato. `chronologyIndex`, quando presente, é um inteiro. Os valores de enum são nomes textuais; diferenças entre maiúsculas e minúsculas são normalizadas, mas valores numéricos não substituem os nomes dos tipos.

## Limites e comportamento de gravação

| Limite | Valor |
| --- | --- |
| Corpo JSON | 2 MiB (2.097.152 bytes) |
| Unidades | 500 por pacote |
| Entidades | 500 por pacote |
| Aliases | 20 por entidade |
| Fatos | 100 por entidade |
| Relações | 5.000 por pacote |

| Texto | Máximo de caracteres |
| --- | --- |
| Título do livro, autor, título da unidade, atribuição e rótulo de relação | 300 |
| Rótulo seguro da unidade | 100 |
| Slug e suas referências | 160 |
| Nome da entidade, alias e identificador externo do livro | 200 |
| Descrição da unidade e URL | 2.000 |
| Resumo da entidade e pista de memória | 1.000 |
| Conteúdo do fato | 4.000 |
| Tipo da relação, provider e idioma do livro | 80 |
| ISBN-10 e ISBN-13 | 10 e 13, respectivamente |

O servidor também valida textos, tipos, slugs, referências, ordem e hierarquia antes de gravar. Um arquivo que respeita os limites de quantidade ainda pode ser recusado por uma referência inválida ou por conteúdo incompatível com a edição. A seleção do arquivo não altera o banco, e a resposta de validação não autoriza uma importação posterior sem nova conferência.

Uma importação nova exige que índice e lore estejam em `None` ou `Draft` e que o livro não tenha conteúdo editorial. Ela cria as unidades e o conhecimento em `Draft`, sem alterar os vínculos pessoais do livro. Pedidos de memory pack continuam pendentes até a publicação do lore, conforme o workflow editorial.

Se o pacote for idêntico ao conteúdo existente, repetir a importação retorna sucesso sem gravação e conserva GUIDs e estados de publicação. Se houver conteúdo diferente, a importação retorna conflito: não mescla, não apaga e não substitui registros. A interface de autoria continua sendo o caminho para alterar o rascunho existente. Substituição, histórico, diffs e migração entre versões do formato ficam para evoluções futuras.

## API administrativa

Todas as rotas usam `/api/admin/books/{bookId}/memory-pack`. Exigem a policy `Admin`, usam respostas privadas sem cache e respeitam a exclusão de livros privados. Requisições `POST` também exigem o token antiforgery `X-CSRF-TOKEN` obtido por `GET /api/auth/csrf`.

| Método e sufixo | Resultado |
| --- | --- |
| `GET /export` | Exporta o JSON v1 atual ou um modelo vazio ligado ao livro |
| `POST /validate` | Recebe o documento JSON e devolve `{ isValid, alreadyImported, counts, issues }`, sem gravar |
| `POST /import` | Valida novamente e cria o rascunho de forma atômica, devolvendo `{ alreadyImported, counts }` |

`counts` contém `readingUnits`, `entities`, `aliases`, `facts`, `relations` e `events`; `events` é o subconjunto de entidades do tipo `Event`. Cada item de `issues` contém `path` e `message`, indicando o campo ou referência a corrigir. Uma validação bem-sucedida sem problemas tem `isValid: true` e `issues: []`.

Uma importação nova retorna `201`; uma repetição idêntica retorna `200`, e conteúdo existente diferente retorna `409`. Um documento recusado na importação retorna `422` com o relatório de validação. Livro ausente ou privado retorna `404`. Erros de documento ou incompatibilidade impedem a gravação. O limite de tamanho se aplica antes do processamento integral do arquivo.

## Exemplo original

O arquivo [examples/memory-pack-v1.json](examples/memory-pack-v1.json) contém **As Cartas do Farol**, de **Equipe Mnemora**: três capítulos, quatro entidades, um alias futuro, cinco fatos e duas relações. É um universo fictício original feito para demonstração; não representa o conteúdo de um livro real nem deve ser vinculado a uma edição externa.

Para experimentar, crie no Admin um livro compartilhado vazio com o título **As Cartas do Farol** e o autor **Equipe Mnemora**. O exemplo omite idioma e identificadores, como o cadastro curado simples da interface. Baixe seu modelo, confira o bloco `book` e importe o exemplo. Revise os pontos antes de publicar. O comportamento esperado para conferir a curadoria é:

| Progresso | Conteúdo esperado após publicação |
| --- | --- |
| Sem unidade | Nenhum conhecimento do pacote |
| Capítulo 1 | Lia e Farol do Cabo, com seus primeiros fatos; nenhuma facção, evento, relação ou alias futuro |
| Capítulo 2 | Guardiões da Maré, o fato do selo e a relação de colaboração; o alias e o evento final continuam ocultos |
| Capítulo 3 | O alias “A mensageira da luz”, o evento de reacendimento e sua relação com o farol |
| Retrocesso ao capítulo 1 | Facção, fato do selo, relações, alias e evento posterior voltam a ficar ocultos |

A tela administrativa pode exibir o conteúdo completo para autoria e revisão. Essa tabela descreve o que o leitor recebe das rotas seguras após a publicação; não significa que o JSON completo possa ser enviado às telas de leitura.

## Geração assistida opcional

O Admin pode produzir este mesmo JSON v1 usando [resumos fornecidos por capítulo](assisted-generation.md). A funcionalidade exige livro compartilhado vazio, configuração do Groq no backend e revisão humana. Evidências, cobertura, diagnósticos e hash ficam em um relatório separado: não adicione esses campos ao documento de importação. O fluxo atual de validação, idempotência, conflito e importação em rascunho permanece responsável pela gravação. Editar o JSON invalida a correspondência automática com o relatório original.
