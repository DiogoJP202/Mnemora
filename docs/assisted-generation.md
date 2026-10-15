# Geração assistida de pacotes editoriais

O recurso **Gerar pacote com IA**, no detalhe administrativo de um livro, transforma resumos fornecidos por capítulo em um [pacote JSON v1](memory-pack-format.md). O resultado tem evidências em um relatório separado e segue a validação/importação já existente, sempre em rascunho. A geração não publica conteúdo, não altera o contrato v1 e não cria migrations.

## Configuração

O padrão é desabilitado. Configure no processo **da API ASP.NET Core**, por variáveis de ambiente, configuração do host ou .NET user secrets:

```dotenv
MemoryPackGeneration__Enabled=true
MemoryPackGeneration__Provider=Groq
MemoryPackGeneration__Model=openai/gpt-oss-120b
MemoryPackGeneration__ApiKey=
```

Preencha a chave somente no armazenamento de segredos do ambiente. O bloco acima não é uma configuração funcional sem chave. A API não carrega `.env` automaticamente; `.env.example` documenta os nomes. Não use prefixo `NEXT_PUBLIC_`, não coloque a chave em `apps/web/.env.local` e não a registre em logs. Ausência de chave, configuração inválida, modelo ou provedor diferente mantêm o recurso indisponível. Não há descoberta de modelos, URL de provedor configurável ou fallback.

O objetivo operacional é custo de API zero. **Antes da habilitação, confirme no console que a organização está no Groq Free, sem upgrade pago.** O aplicativo não conhece o plano de cobrança nem impede cobrança em uma conta paga. Confira os [limites efetivos](https://console.groq.com/docs/rate-limits) e o [faturamento](https://console.groq.com/docs/billing-faqs) na organização. Os limites publicados e a disponibilidade de modelos podem mudar; os limites locais não garantem a capacidade gratuita de um livro inteiro.

Os resumos são enviados ao Groq. Confira o [tratamento de dados](https://console.groq.com/docs/your-data) e configure Zero Data Retention na organização antes de usar material sensível. Use somente fontes cujo envio seja autorizado. O [schema estrito](https://console.groq.com/docs/structured-outputs) garante uma estrutura de resposta suportada, não a verdade nem a segurança editorial do conteúdo.

No Render, configure os mesmos nomes em **Environment** do serviço e aplique o deploy. Nenhuma chave foi adicionada ao Blueprint e nenhuma API real foi habilitada para os testes desta entrega. Sessões são efêmeras mesmo se o banco estiver em um disco persistente. Um restart, redeploy, suspensão ou troca de processo perde fontes e resultados retidos na memória; baixe os artefatos durante o trabalho. O MVP exige uma única instância do backend.

## Preparar e gerar

1. Entre com um Admin e abra um livro compartilhado, com a identidade bibliográfica salva. Livros privados, publicados ou com qualquer unidade, entidade ou relação já cadastrada são recusados. Mesmo um índice em rascunho torna o livro preenchido. Para esses livros, use exportação e editores atuais.
2. Cole fontes com cabeçalhos consecutivos desde o capítulo 1. Use uma linha vazia entre parágrafos. Preserve um capítulo sem resumo como cabeçalho vazio para que a lacuna seja explícita:

   ```text
   ## Capítulo 1 — A chegada
   Resumo limitado ao que foi revelado neste capítulo.

   ## Capítulo 2 — A travessia

   ## Capítulo 3 — O encontro
   Resumo deste capítulo, sem reescrever a apresentação inicial.
   ```

3. Clique **Conferir capítulos e lacunas**. Corrija no lote os títulos, resumos e sequência e confira novamente. Números duplicados, saltos ou ordem incorreta bloqueiam a entrada; não são renumerados silenciosamente. Para uma edição Open Library com sumário, a consulta opcional ajuda a conferir os títulos. Só um sumário simples, com todos os itens no primeiro nível, pode ser copiado para o formulário vazio; hierarquias exigem conferência manual. Consultar não grava unidades.
4. Informe atribuição e URL HTTPS opcional da fonte narrativa, confirme a edição e indique se o índice está completo. O URL é registrado como procedência e **não é acessado**. Sem confirmação de índice completo, o resultado será parcial; o sistema não tenta descobrir capítulos finais omitidos.
5. Clique **Gerar rascunho**. O navegador solicita uma etapa por vez ao backend e respeita o próximo horário permitido. Fontes ficam congeladas na sessão. Não há job em segundo plano que continue chamando o provedor depois que a página fecha; a chamada corrente pode ser interrompida pela desconexão, preservando o último prefixo aceito.
6. Acompanhe unidades, chamadas, tokens e alertas. Baixe os resultados intermediários quando aparecerem. Em falha ou quota, use **Consultar progresso** e, quando permitido, **Continuar geração**; não há repetição automática após erro. **Cancelar geração** sinaliza a chamada corrente e impede novas etapas da sessão. A vaga só é liberada quando a chamada termina.

Há [fontes fictícias originais](examples/generation-sources.txt) para uma avaliação pequena. Elas não devem ser vinculadas a livros reais. Nenhum conteúdo externo é completado por conhecimento próprio do modelo.

## Revisar e importar

Cada unidade mostra cobertura e candidatos aceitos, com trecho do JSON, parágrafo, citação e justificativa. Os downloads são:

- **JSON original:** documento estrito v1, sem relatório, fontes integrais, sessão ou dados pessoais. A identidade é copiada pelo servidor do cadastro do livro. `documentJson` preserva os bytes UTF-8 canônicos cujo SHA-256 está em `report.packHash`.
- **Relatório original:** cobertura, lacunas, diagnósticos, atribuição/URL, modelo, versão do prompt, consumo, hash do pacote e evidências com hash da fonte, parágrafo, citação, justificativa e caminho do item. Não é um arquivo de importação nem lore para o leitor. O hash da fonte é calculado sobre os parágrafos normalizados da unidade unidos por duas quebras de linha.

Confira especialmente nomes e descrições iniciais, suspeitas atribuídas, identidades reveladas depois e eventos antigos explicados em capítulos posteriores. Uma citação presente confirma a origem textual, mas o próprio resumo pode conter um spoiler retrospectivo. Não há verificador semântico definitivo.

Marque a revisão e, quando parcial, o reconhecimento das lacunas antes de **Enviar para validação abaixo**. Na transferência existente, pode editar o JSON ou selecionar novamente um arquivo. Qualquer edição invalida validação e confirmações; o aviso informa que o relatório e seu hash pertencem à versão original. Downloads do JSON revisado não atualizam o relatório. Se selecionar um arquivo externo, sua procedência e eventual incompletude passam a depender da revisão manual; o v1 não carrega essa informação.

Clique **Validar arquivo**, confira contagens e erros, confirme a edição e então **Importar como rascunho**. Sem pelo menos uma unidade e uma entidade válida, o arquivo é apenas material de trabalho; a geração não inventa entidades para satisfazer o importador. A validação estrutural não certifica a veracidade do texto.

A importação verifica novamente o destino em transação, cria índice e conhecimento em `Draft` e mantém metadados e dados dos leitores. Reenvio idêntico preserva IDs e estados; conteúdo existente diferente é recusado. Use os editores e o preview por unidade e publique manualmente índice e lore quando revisados. Depois da importação o destino deixa de ser elegível para gerar: preserve os downloads, pois a entrega de resultado da sessão também revalida essa elegibilidade.

## Montagem e antisspoiler

`MemoryPackGenerationTarget` reutiliza a identidade exportada e verifica livro compartilhado vazio. `MemoryPackGenerationService` coordena a etapa. `GroqMemoryPackGenerationProvider` usa `HttpClient`, endpoint fixo, JSON Schema estrito, resposta limitada e sem ferramentas/streaming. `MemoryPackGenerationAssembler` produz o v1 por código. `MemoryPackGenerationSessionStore` mantém fontes e resultados em memória com limites próprios; ele não usa o cache público de metadados.

Só o resumo corrente e o registro compacto de entidades/aliases **já conhecidos** entram na chamada. Títulos e resumos futuros não são enviados. Evidências precisam referenciar parágrafos correntes e citações literais verificáveis. Candidatos sem suporte textual/referências válidas são descartados e diagnosticados. A aplicação atribui slugs, ordem e unidade de revelação, não o modelo.

Nome, tipo, descrição e apresentação inicial de uma entidade permanecem congelados. Informação posterior entra como fato ou alias posterior; personas não são fundidas automaticamente. Descrições iniciais e pistas são aceitas somente como trechos literais sustentados, ou omitidas com diagnóstico. Suspeitas exigem atribuição; identidades e relações apenas suspeitadas são retidas para revisão. Repetições exatas preservam a primeira revelação. Eventos retrospectivos são revelados onde foram comprovados, sem retrodatação. `imageUrl` e `chronologyIndex` ficam `null`; importância inicial é `1`. Não há chamada final que reescreva o livro inteiro.

Essas regras reduzem contaminação entre capítulos, mas não comprovam semanticamente afirmações ou nomes. O contrato v1 não representa fusão de identidades dependente do progresso nem substituição automática de fatos antigos. A curadoria continua responsável pela redação atribuída e temporal. Após publicar, as projeções do `KnowledgeReader` continuam filtrando lore antes dos DTOs do leitor, incluindo Recall e timeline.

## API administrativa

Base: `/api/admin/books/{bookId}/memory-pack/generations`. Todas as rotas exigem Admin e retornam `no-store`. Escritas exigem o token antiforgery atual em `X-CSRF-TOKEN` e estão sujeitas ao rate limit de escrita. Outro Admin recebe `404` ao consultar uma sessão alheia.

| Método e sufixo | Entrada e comportamento |
| --- | --- |
| `GET /availability` | Disponibilidade, motivo, limites e referência da sessão do próprio Admin |
| `POST /parse` | `{ "text": "## Capítulo 1 — ..." }`; separa fontes e retorna problemas, sem criar sessão |
| `POST /` | `sources: [{title, summary}]`, `attribution`, `sourceUrl` nulo ou HTTPS, `editionConfirmed`, `indexComplete`; cria sessão e retorna `201` |
| `GET /{runId}` | Estado, revisão, cobertura e diagnósticos; não faz chamada de IA |
| `POST /{runId}/next` | Somente `operationId` UUID e `expectedRevision`; cursor, edição e acumulado vêm do servidor |
| `GET /{runId}/result` | `status`, `document`, `documentJson`, `report`, `validation`; inclusive prefixo parcial |
| `POST /{runId}/cancel` | Cancela e preserva o último resultado concluído |

JSON de escrita é estrito: campos extras/duplicados, obrigatórios ausentes e tipos inválidos são rejeitados. O corpo HTTP tem teto de 2 MiB e profundidade 64; fontes têm seu teto menor. Provedor, modelo, cursor, revelações e documento não são aceitos como parâmetros do navegador.

Uma operação repetida devolve seu recibo, inclusive durante processamento, sem segunda chamada. Revisão antiga ou operação concorrente não inicia uma nova extração. Os recibos são limitados, não um histórico ilimitado de documentos. Edição alterada, publicação ou inclusão de conteúdo durante a geração encerra o uso do alvo com conflito; nenhuma transação SQLite é mantida durante HTTP.

## Limites e falhas

Todos os nomes abaixo usam o prefixo de ambiente `MemoryPackGeneration__`. Limites podem ser reduzidos; ultrapassar os tetos de segurança torna a configuração indisponível.

| Opção | Padrão / teto |
| --- | --- |
| `MaxUnits` / `MaxSourceBytes` | 64 / 524.288 bytes UTF-8 |
| `MaxDocumentBytes` / `MaxReportBytes` | 2.097.152 bytes cada |
| `MaxSessionBytes` / `MaxGlobalBytes` | 8.388.608 / 16.777.216 bytes |
| `MaxSessions` | 2 globais; sempre uma por Admin |
| `TimeoutSeconds` | 90; uma chamada global simultânea |
| `IdleMinutes` / `LifetimeMinutes` | 30 inativa / 120 absolutos |
| `MaxAttemptsPerUnit` / `MaxCalls` | 2 / 80 por sessão |
| `MaxInputTokens` / `MaxOutputTokens` | 4.000 estimados / 2.500 por chamada |
| `MaxTokens` | 180.000 por sessão; também orçamento preventivo global em janela de 24h da instância |
| `MinCallIntervalSeconds` | 60 por padrão; configurável de 0 a 86.400 |

O teto de memória contabiliza dados UTF-8 retidos e metadados limitados, não todo o heap/overhead do runtime. Cada sessão reserva sua capacidade para evitar concorrência que exceda o teto global. A entrada é estimada com instruções, schema, resumo e registro conhecido, usando bytes/3 mais margem; não é o tokenizer do modelo. Não há truncamento silencioso de fontes ou registros para caber.

O consumo retorna do provedor quando disponível, incluindo tokens de raciocínio no total. Antes da chamada, o serviço reserva entrada estimada mais saída máxima. Falhas sem uso verificável mantêm uma estimativa conservadora contabilizada. Contadores globais e cooldown permanecem entre sessões no mesmo processo, para impedir contornar limites criando outra sessão; não sobrevivem a reinício. Não representam um medidor financeiro nem a quota compartilhada da organização.

Timeout, recusa, resposta truncada/malformada, tamanho excessivo e falta de evidência não apagam capítulos anteriores. Uma falha não avança o cursor. `429` pausa conforme `Retry-After` e `nextAllowedAt`; a retomada é explícita. Recibos de erro retornam o erro registrado; uma nova tentativa usa outro `operationId` e continua sujeita às quotas. Duas falhas na mesma unidade esgotam as tentativas e deixam o prefixo disponível para revisão/download.

Se o Groq responder `401`, o aplicativo orienta conferir a chave no backend; `403` orienta conferir permissões da chave/modelo. Esses problemas do provedor chegam como `503` da aplicação, sem confundir a credencial do serviço com a sessão do Admin. O corpo de erro externo não é lido nem repassado. Ao usar `Read-Host`, conserve o texto da pergunta e cole a chave somente quando o terminal solicitar a entrada; o argumento entre aspas é apenas o rótulo da pergunta. Reinicie a API após trocar variáveis do processo.

Sessões expiradas retornam `404`. Cancelamento ou expiração sinaliza a chamada ativa, mas não permite substituí-la enquanto ela não tiver terminado. Recarregar a página pode recuperar a referência pelo backend enquanto ele conservar a sessão; não recupera fontes em um processo reiniciado. O formulário e o resultado ficam em memória no navegador, sem persistência em localStorage, sessionStorage, service worker, banco ou arquivos de jobs. Somente os downloads solicitados são arquivos persistentes sob controle do Admin.

O Next.js usa `experimental.proxyTimeout: 120000`, superior ao timeout do provedor. Logs da geração incluem sessão, unidade, modelo, versão do prompt, duração, tokens e categoria de erro, sem textos integrais, respostas ou credenciais.

## Verificação e avaliação

CI e testes locais usam provedores HTTP e relógios falsos, sem chave/API real. Os testes .NET abrangem parser, alvo, schema e limites do provedor, evidências, montagem, isolamento de prefixos, sessões, Admin/CSRF, concorrência, idempotência, quotas, cancelamento e expiração. O pacote gerado atravessa a validação/importação atuais e as projeções antes/no ponto/depois e em retrocesso, incluindo Recall e timeline. Uma propriedade compara a projeção até `k` do pacote completo com o pacote gerado apenas até `k`, sem depender dos IDs.

Na avaliação local de navegador, uma chamada fictícia de 35 segundos atravessou a origem do Next.js, completando quatro capítulos; os downloads tiveram hashes correspondentes e foram importados em rascunho. Também foram conferidos cancelamento, recuperação de referência após reload, falha de rede com retomada manual, edição que invalida confirmações, parcial com fonte ausente e confirmação obrigatória, desktop, 360 px e temas claro/escuro. Respostas autenticadas de estado, avanço e resultado mantiveram `no-store`. Isso comprova integração e fronteiras com dados controlados, **não** a qualidade, latência ou capacidade real do Groq Free. Nenhum consumo real foi medido nesta entrega.

Antes de avaliar material real, comece com poucas unidades fictícias e meça uso retornado, estimativas, duração e descartes no relatório. Revise o pacote contra as fontes e o preview. Não prometa completar um livro denso apenas pela quantidade de capítulos. Consulte o [relatório de entrega](final-report.md) e o [plano aprovado](assisted-generation-plan.md).
