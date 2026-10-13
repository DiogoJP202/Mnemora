# Geração assistida de pacotes editoriais — fases 18 a 22

Este plano implementa a melhoria aprovada para gerar conteúdo editorial a partir de fontes narrativas coladas pelo Admin. O fluxo permanece ligado a um livro e à sua edição: **livro compartilhado vazio → fontes por capítulo → extração incremental → JSON v1 e relatório → validação existente → revisão → importação em Draft → editores e preview existentes → publicação manual**.

O primeiro provedor será **Groq Free, com GPT-OSS 120B**. O orçamento autorizado para API é **zero**: não há fallback pago, troca automática de provedor nem chamadas reais nos testes ou no CI. A chave fica exclusivamente no backend, fora do Git e dos logs. Os limites locais são estimativas preventivas de consumo, não confirmação do plano da conta, da disponibilidade gratuita ou do faturamento do provedor. A configuração deve permanecer no plano gratuito; indisponibilidade ou quota esgotada interrompe a geração com diagnóstico.

## Escopo e componentes reutilizados

- O contrato de importação continua sendo [JSON v1](memory-pack-format.md), definido em `src/Mnemora.Application/MemoryPackDocuments.cs`. Não adicionar evidências, confiança, IDs de sessão, campos de publicação ou diagnósticos ao documento.
- `MemoryPackTransfer` continua responsável pela validação da edição, referências e limites, importação atômica em rascunho, repetição idêntica e conflito diante de conteúdo diferente.
- A geração aceita somente livro compartilhado sem qualquer unidade, entidade ou relação editorial. Um índice existente também torna o alvo inelegível; não haverá preenchimento por merge, sobrescrita nem substituição.
- O Admin oferece revisão simples do resultado e das evidências antes da importação. As correções completas depois de importar usam os editores de unidades, entidades, fatos, aliases e relações já existentes, com o preview atual.
- A política antisspoiler, os estados independentes de índice e conhecimento, autenticação Admin, antiforgery e respostas privadas sem cache são preservados.
- As sessões são temporárias na memória do backend, com posse vinculada ao Admin e à edição. Não há migration, gravação de fontes no banco, fila durável ou recuperação automática após reinício.

Ficam fora do MVP: crawlers, busca automática de fontes narrativas, extração arbitrária de arquivos, vetorização, plataforma de agentes, novo editor editorial, novo formato de importação e publicação automática. Open Library e Google Books continuam úteis para metadados; um ISBN não comprova o enredo nem a divisão narrativa da edição.

## Fontes e processamento incremental

O Admin cola um lote de texto com cabeçalhos `## Capítulo 1 — Título`, seguido do resumo daquele capítulo. Cada cabeçalho delimita uma unidade. O administrador confirma a edição e informa se o índice apresentado está completo. Um capítulo listado sem resumo é uma fonte ausente explícita; capítulos omitidos, especialmente os finais, não podem ser descobertos com segurança apenas pelos cabeçalhos. A interface deve separar índice confirmado, cobertura das fontes e conclusão do processamento.

O material original [generation-sources.txt](examples/generation-sources.txt) usa quatro capítulos do livro fictício **As Máscaras do Cais**, de **Equipe Mnemora**, e pode ser copiado no fluxo de geração para testes. Ele não representa um livro publicado nem deve ser associado a uma edição externa.

O backend cria slugs, ordem absoluta e rótulos seguros das unidades por código. A identidade `book` vem do cadastro/exportação da edição, não da IA. Títulos de capítulos futuros e resumos posteriores não entram na extração de uma unidade inicial.

Cada avanço da sessão processa apenas a unidade corrente e o registro compacto das entidades e aliases já conhecidos no prefixo. O provedor devolve candidatos estruturados com evidências de parágrafos da unidade corrente. Esse contrato interno de geração não substitui o JSON v1. A aplicação confere o formato, as referências e a presença literal das citações, atribui revelações à unidade corrente e monta o pacote. Citação válida comprova origem textual, não verdade ou segurança semântica.

As regras de montagem são:

1. Nome, slug, tipo, descrição inicial e primeira unidade conhecida são fixados na primeira apresentação segura. Não reescrever esses campos com informação de capítulos posteriores. Usar importância neutra no MVP e omitir imagens.
2. Identidade revelada depois entra como alias e/ou fato com a unidade posterior. Uma descrição ampliada também entra como fato posterior.
3. `memoryHint` usa somente conhecimento sustentado na revelação do respectivo fato. Se houver dúvida, omitir; nunca regenerar uma pista inicial usando o final do livro.
4. Relações exigem evidência atual, participantes conhecidos e revelação nunca anterior à apresentação deles. Uma relação descoberta depois não é retrodatada porque os participantes já existiam.
5. Um evento do passado explicado agora é conhecido agora. `chronologyIndex` será omitido no MVP: é estático, sem revelação própria, e a cronologia descoberta posteriormente não pode alterar silenciosamente a apresentação inicial.
6. Suspeitas e informações atribuídas permanecem atribuídas no texto. A explicação posterior é acrescentada com sua revelação; o v1 não possui substituição temporal de fatos ou relações anteriores.
7. Repetições exatas podem ser deduplicadas preservando a primeira redação segura e evidência suficiente. Nomes parecidos, homônimos e personas cuja identidade comum só é descoberta depois não justificam fusão automática. Manter personas separadas quando reuni-las exporia seus fatos cedo demais.
8. Candidato sem evidência suficiente vai para diagnóstico/lacuna, não para lore. A montagem final é determinística e não usa uma chamada que reescreva globalmente o pacote a partir do livro inteiro.

Fontes são dados não confiáveis, separados das instruções do provedor. O modelo não recebe ferramentas de navegação, execução, importação ou publicação. Instruções embutidas na fonte, como pedidos para ignorar regras, não concedem capacidade de ação. Conhecimento próprio do modelo sobre um livro não serve para completar capítulos ou fatos ausentes.

## Resultado, evidências e conteúdo parcial

O resultado contém dois artefatos separados: o `MemoryPackDocument` v1 e um relatório administrativo de geração. O relatório identifica cobertura por unidade, fontes/parágrafos, citações curtas, justificativas, lacunas, alertas e os itens do documento correspondente. Deve vincular caminhos do resultado à versão/hash do documento; mudanças e reordenações podem tornar caminhos antigos inválidos. O relatório pode ser consultado e baixado antes que a sessão expire. Ele não integra o pacote importado e não é enviado às telas do leitor.

Um resultado parcial é explicitamente incompleto. Fontes ausentes não são confundidas com capítulos processados, e falha de processamento não avança silenciosamente o cursor. Informação só atestada em uma unidade posterior recebe essa unidade, mesmo que mencione um evento anterior sem fonte. Nunca inventar entidades para cumprir o mínimo do importador. Um parcial somente pode seguir à importação se passar pela validação existente, incluindo ao menos uma unidade e uma entidade, e o Admin reconhecer a incompletude.

A revisão separa três níveis: validação estrutural atual; verificações editoriais adicionais; revisão humana. Um pacote estruturalmente válido pode conter um resumo incorreto, uma afirmação não comprovada ou um spoiler. Uma auditoria semântica por outra IA é opcional e não constitui prova definitiva.

A importação sempre revalida o destino e conserva seu comportamento atual. Alteração da edição ou inclusão de conteúdo editorial durante a sessão impede a utilização daquele resultado para uma nova importação. Depois de importar, o relatório representa a versão gerada: uma correção nos editores não herda automaticamente a evidência antiga. A publicação continua manual pelos controles existentes.

## Limites do MVP

Os limites temporários abaixo são adicionais aos limites oficiais do [JSON v1](memory-pack-format.md). Devem ser validados no backend antes de alocar recursos ou iniciar uma chamada.

| Recurso | Limite aprovado |
| --- | --- |
| Unidades por geração | 64 |
| Fontes coladas | 512 KiB |
| Documento final | 2 MiB |
| Relatório de geração | 2 MiB |
| Memória de uma sessão | 8 MiB |
| Memória global de sessões na instância | 16 MiB |
| Sessões simultâneas na instância | 2 |
| Sessões simultâneas por Admin | 1 |
| Chamadas simultâneas ao provedor na instância | 1 |
| Timeout por chamada | 90 segundos |
| Expiração por inatividade | 30 minutos |
| Vida máxima da sessão | 120 minutos |
| Tentativas por unidade | 2 |
| Chamadas por sessão | 80 |
| Intervalo mínimo entre chamadas | 60 segundos |
| Saída máxima por chamada | 2.500 tokens |
| Entrada estimada máxima por chamada | 4.000 tokens |
| Orçamento estimado por sessão | 180.000 tokens |

Esses tetos não garantem que toda geração de 64 unidades caberá no orçamento ou terminará antes da expiração. Uma quota do provedor pode ser menor ou estar compartilhada com outros usos da conta. Não truncar fontes, registro conhecido, evidências ou saída silenciosamente para continuar; interromper e mostrar qual limite foi atingido. O intervalo deve orientar o próximo avanço da UI, sem manter uma requisição HTTP dormindo até a próxima chamada. Uma repetição também consome limites e orçamento.

Os limites globais são da instância/processo que mantém as sessões. O MVP assume uma única instância do backend; múltiplas réplicas exigiriam outra coordenação. Reinício, deploy ou expiração elimina a sessão, e o Admin deverá preservar os artefatos por download e iniciar uma nova geração quando necessário. Cancelamento encerra a operação corrente e impede as próximas; nenhuma unidade ou lore é gravada durante a geração.

Registrar apenas identificação da sessão/livro, estado, unidade, duração, versão de prompt, modelo e consumo estimado/retornado. Não registrar chave, prompt completo nem texto integral das fontes. Medir duração, tokens e frequência de falhas com material fictício para avaliar viabilidade e futuro ajuste dos limites. Não apresentar estimativas locais como garantia de faturamento zero.

## Fases e gates

Cada fase termina com verificações pertinentes, inspeção do diff e de segredos, commit e push conforme o [plano de implementação](implementation-plan.md). **A fase 19 só começa após commit e push bem-sucedidos da fase 18**; o mesmo gate se aplica às fases seguintes. Nenhum teste automatizado chama uma API real.

| Fase | Objetivo e componentes | Dependência | Aceite e testes |
| --- | --- | --- | --- |
| 18 — Contratos e fontes | Definir contratos internos de geração, opções/limites, parsing do lote de fontes e material fictício; documentar o escopo sem alterar o JSON v1 | Plano aprovado | Parsing preserva ordem e fontes ausentes; rejeita lote inválido/fora dos limites; contratos distinguem candidatos, evidências, relatório e documento |
| 19 — Provedor e montagem antisspoiler | Implementar abstração simples de provedor, cliente Groq Free configurável e montagem incremental em memória | Commit e push da 18 | Provedor falso cobre sucesso, JSON malformado, timeout e erro; evidência inválida não vira lore; introdução é congelada, alias/relação/evento recebem unidade correta e duplicação não contamina o prefixo |
| 20 — Sessões e API protegida | Implementar sessão temporária, posse, quotas, expiração, cancelamento e endpoints Admin de início/avanço/resultado | Commit e push da 19 | Testes de isolamento, autorização, antiforgery, livro privado/preenchido, limite global, repetição de avanço, cancelamento e expiração; fontes e resultados não são persistidos no banco |
| 21 — Admin | Integrar fontes em lote, confirmação da edição/índice, progresso, erros, parcial explícito e revisão/download dos artefatos ao fluxo de transferência existente | Commit e push da 20 | Build/lint e jornada responsiva; importação usa validação e confirmação atuais; revisão completa em Draft usa editores/preview existentes; geração não publica |
| 22 — Qualidade e entrega | Executar testes integrados, verificar fronteiras, regressões, documentação/configuração e comportamento operacional | Commit e push da 21 | Suíte atual continua passando; pacote fictício atravessa validador/importador; antes/no ponto/depois e retrocesso preservam ausência de spoilers; CI funciona sem chave/API real e nenhuma migration é criada |

Os cenários fictícios devem verificar a suspeita atribuída no capítulo 1, repetição no 2, relação descoberta no 3, identidade e evento antigo revelados no 4. Para um provedor falso controlado, o conteúdo visível até a unidade `k` do pacote completo deve equivaler ao conteúdo visível de uma geração limitada ao prefixo até `k`, desconsiderando IDs de banco. Também testar fonte ausente, índice não confirmado, fato sem suporte, personas ambíguas, descrição/pista contaminada, interrupção e conflito se o alvo deixar de estar vazio.

## Riscos que permanecem sob curadoria

- Um resumo por capítulo pode usar conhecimento retrospectivo: uma citação literal do “futuro traidor” no capítulo inicial continua sendo um spoiler. Extração por prefixo reduz contaminação, mas não prova que a fonte preserva a experiência de leitura.
- Fontes podem corresponder a outra tradução/edição, omitir o final ou trazer fatos inventados. Confirmação bibliográfica e cobertura explícita não substituem leitura/revisão do material.
- O v1 não representa validade temporal ou união de identidades condicionada ao progresso. Redação atribuída/temporal e preservação de personas são necessárias para evitar que consolidação produza revelações antecipadas.
- Sessões em memória e o plano gratuito podem interromper livros longos. O MVP oferece falha explícita, limites e download; jobs duráveis só serão avaliados se medições demonstrarem necessidade.
