# Preparação automática de fontes por capítulo

O Admin pode coletar um índice e sinopses públicas para preparar a geração existente, sem escrever os resumos à mão. O adaptador atende somente às quatro obras abaixo. A tela informa a cobertura do catálogo do servidor antes de iniciar. Ele usa o guia comunitário do Wikibooks, não o texto integral dos romances, nem descrições bibliográficas como conhecimento narrativo.

| Chave interna | Obra | Índice utilizado | Capítulos esperados |
|---|---|---|---:|
| `hp1` | Harry Potter e a Pedra Filosofal | [Wikilivros: A Pedra Filosofal](https://pt.wikibooks.org/wiki/Guia_dos_Trouxas_para_Harry_Potter/Livros/A_Pedra_Filosofal) | 17 |
| `hp2` | Harry Potter e a Câmara Secreta | [Wikilivros: A Câmara Secreta](https://pt.wikibooks.org/wiki/Guia_dos_Trouxas_para_Harry_Potter/Livros/A_C%C3%A2mara_Secreta) | 18 |
| `hp3` | Harry Potter e o Prisioneiro de Azkaban | [Wikilivros: O Prisioneiro de Azkaban](https://pt.wikibooks.org/wiki/Guia_dos_Trouxas_para_Harry_Potter/Livros/O_Prisioneiro_de_Azkaban) | 22 |
| `hp5` | Harry Potter e a Ordem da Fênix | [Wikibooks: Order of the Phoenix (inglês)](https://en.wikibooks.org/wiki/Muggles%27_Guide_to_Harry_Potter/Books/Order_of_the_Phoenix) | 38 |

As páginas são fontes da **obra**. Elas não identificam o ISBN ou asseguram correspondência de tradução, paginação e edição. Essa confirmação permanece com o administrador. Outras obras continuam com a entrada manual; o sistema não pede à IA que invente capítulos para preencher lacunas.

## Coleta e fronteira de conhecimento

1. Consultar somente os endpoints oficiais `https://pt.wikibooks.org/w/api.php` e `https://en.wikibooks.org/w/api.php`, com idioma e título de índice na allowlist do servidor. Ordem da Fênix usa o índice inglês; os três primeiros volumes usam o português.
2. Obter a revisão e o índice de seções por `action=parse&prop=tocdata|revid&format=json&formatversion=2&maxlag=5`.
3. Localizar uma única seção `Capítulos` ou `Chapters` de nível 2, conforme o idioma configurado. Obter exclusivamente essa seção com `oldid` fixado à revisão anterior e `prop=text|revid`.
4. Extrair os links de capítulos dessa seção. Validar prefixo da obra, números, duplicados, contagem e sequência completa. Preservar o título real da página: o capítulo 11 de Azkaban está em `/Capítulo11`, sem espaço. Caminhos não são inferidos apenas do número.
5. Para cada capítulo, consultar sua tabela de seções e localizar exclusivamente `Sinopse` ou `Synopsis`, nível 2. Obter somente essa seção na mesma revisão. Rejeitar página, ID ou revisão divergentes. Ordem da Fênix segue diretamente os caminhos ingleses presentes no índice, sem presumir páginas portuguesas a partir do número.
6. Se a página ou a sinopse em português estiver ausente ou editorialmente inutilizável, tentar `Synopsis` do capítulo equivalente em inglês. Registrar o idioma efetivamente usado. Falhas operacionais, bloqueios, 429, timeout e resposta inválida **não** provocam troca de fonte ou repetição automática.
7. Converter o HTML em parágrafos de texto visível com AngleSharp, sem carregador de recursos ou execução de scripts. Excluir navegação, tabelas, imagens, conteúdo oculto e referências. Destinos e atributos dos links não entram no texto: um nome visível pode apontar para uma identidade revelada posteriormente.
8. Entregar as fontes à geração incremental atual. A IA recebe a sinopse corrente e o conhecimento permitido; não recebe análises ou capítulos futuros.

`Analysis`, `Questions`, `Greater Picture`, `Connections` e as páginas gerais de personagens, lugares e eventos não são fontes de geração. O guia pode mencionar finais e outros volumes nessas seções. A [introdução do projeto](https://en.wikibooks.org/wiki/Muggles%27_Guide_to_Harry_Potter/Introduction) explica que os resumos recordam o capítulo, mas o restante do guia inclui análises da série inteira.

Mesmo uma sinopse pode conter erro, interpretação retrospectiva ou informação antecipada. Evidência textual comprova origem, não veracidade ou ausência semântica de spoilers. O resultado continua sujeito à revisão, validação e importação em rascunho; publicação é manual.

## Revisão, proveniência e licença

O relatório separado acompanha cada unidade com provedor, idioma, URL da revisão (`oldid`), ID da página, licença, atribuição, instante de coleta, hash SHA-256 da resposta de conteúdo e hash SHA-256 do texto normalizado. Os parágrafos usam a mesma normalização da geração. O hash permite conferir versões e evidências; não certifica correção editorial.

A [licença declarada pelo Wikibooks](https://en.wikibooks.org/w/api.php?action=query&meta=siteinfo&siprop=rightsinfo&format=json&formatversion=2) é [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/). A atribuição aos colaboradores, os links às páginas/revisões e a indicação de extração, organização e eventual tradução acompanham as fontes. O campo `source.attribution` e o relatório preservam esse crédito. A licença admite adaptação sob suas condições de atribuição e compartilhamento pela mesma licença; não constitui autorização geral sobre todo material de terceiros eventualmente citado pelo guia.

As sinopses de terceiros não são incluídas como fixtures no repositório. Os testes automatizados usam textos fictícios e respostas HTTP falsas, sem acessar Wikimedia ou Groq no CI.

## Operação

No livro vazio, use **Gerar pacote com IA → Buscar e preparar fontes**. A disponibilidade da geração inclui `automaticSources`, com suporte ao título/autoria cadastrados, motivo de indisponibilidade e lista de títulos cobertos, sem consulta externa. A tela informa isso antes da coleta e impede iniciar obras sem suporte. A página coleta as unidades em sequência. Confira a correspondência com a edição e use **Gerar pacote com as fontes coletadas**; o documento é entregue à validação existente sem baixar, preencher e reenviar JSON. A alternativa manual permanece expansível. A coleta pode ser cancelada, descartada ou retomada explicitamente após erro; reabrir a página recupera a referência enquanto o mesmo processo conservar a sessão. Uma preparação concluída pode ser descartada para trabalhar em outro livro.

Todas as rotas abaixo usam a base `/api/admin/books/{bookId}/source-preparations`, exigem Admin, respostas `no-store` e antiforgery nas escritas. A preparação pertence ao administrador autenticado; outra conta recebe `404`.

| Método/rota | Comportamento |
| --- | --- |
| `POST /` | Resolve a obra a partir do título/autoria salvos e obtém o índice; não aceita URLs ou fontes do navegador. |
| `GET /current` | Referência e cobertura da preparação do próprio Admin neste livro; `404` se ausente/expirada. |
| `GET /{preparationId}` | Cobertura, prévias limitadas, origem, revisões, hashes e diagnósticos. |
| `POST /{preparationId}/next` | `{operationId, expectedRevision}`; servidor escolhe o próximo capítulo, com recibo de operação repetida. |
| `POST /{preparationId}/generate` | `{operationId, expectedRevision, editionConfirmed, indexComplete}`; copia fontes/procedência do servidor para a sessão de geração atual. Reenvio recupera a sessão criada. |
| `POST /{preparationId}/cancel` | Cancela a leitura corrente e impede novas etapas. A preparação não é substituída enquanto a chamada anterior ainda estiver ativa. |

Uma preparação por Admin, duas globais, uma leitura global ativa por processo, até 64 capítulos, duas tentativas explícitas por capítulo e 130 recibos por preparação. A janela de inatividade é de 30 minutos e o prazo absoluto de duas horas. As fontes retidas, incluindo sua serialização/procedência, têm teto conservador de 512 KiB; cada preparação tem teto de 4 MiB e o par, de 8 MiB. Este orçamento é adicional aos 16 MiB globais das gerações; não equivale a um teto de todo o heap do runtime. O texto grande é liberado da preparação depois da transferência, preservando prévias e referências. Reinício perde ambos os estados; a persistência continua proposta na fase 26.

O início usa o rate limit `import-external` (dez operações por minuto por Admin); os passos usam `memory-write` (trinta por minuto). `429` da fonte aplica cooldown global conforme `Retry-After`, inclusive entre preparações. Revisão antiga ou operação concorrente não faz outra leitura. Falhas preservam fontes anteriores e não causam retry automático. Após duas falhas de uma unidade, a lacuna fica explícita e pode-se continuar às demais.

O navegador espaça o início de etapas sucessivas em pelo menos 2,1 segundos para respeitar a quota de escritas, inclusive ao retomar a mesma coleta. A espera pode ser cancelada. Um `429`, seja do serviço ou da fonte, pausa o fluxo e orienta aguardar e usar **Continuar coleta**; não repete a chamada automaticamente. Outras operações da mesma conta ainda podem consumir a quota compartilhada.

A identidade bibliográfica completa, incluindo editora/data, é verificada antes e depois de HTTP e na transferência. O alvo continua exigindo livro compartilhado vazio, sem publicação. Depois da transferência, a geração mantém o gate de identidade do contrato v1 (título/autoria/provider/IDs/ISBN/idioma); editora/data não fazem parte desse contrato. Nenhuma transação SQLite fica aberta durante HTTP; nenhuma migration, publicação ou gravação editorial ocorre na coleta.

O relatório de geração ganha a lista `sources` por unidade, fora do JSON v1. O importador preserva o crédito global em campos já existentes. Quando o pacote é publicado, o leitor vê créditos genéricos e um link de índice reconhecido, com aviso de spoilers externos. Textos livres ou URLs de capítulos/personagens não são projetados como créditos públicos: o backend aceita somente atribuição fixa e índices reconhecidos, sem conteúdo futuro nos DTOs.

- Clientes HTTP com origens fixas `pt.wikibooks.org` e `en.wikibooks.org`, sem redirecionamentos ou URLs fornecidas pelo navegador.
- `User-Agent` identificável, JSON, `maxlag=5` e respeito a `Retry-After`. Nenhum contorno de bloqueio ou scraping com identidade de navegador.
- Resposta de no máximo 1 MiB, incluindo leitura sem `Content-Length`; cada requisição dispõe de até oito segundos e uma etapa de até 25 segundos.
- Sem truncamento silencioso. Índice ambíguo, revisão divergente e limites excedidos geram diagnóstico.
- Coleta sem custo de API ou nova chave. Geração continua com o Groq configurado no backend, seus limites e sem fallback pago.
- Resultado e cobertura permanecem explícitos. Falhas preservam as unidades já coletadas; reinício do serviço perde a sessão temporária.

Referências oficiais: [Action API: parsing](https://www.mediawiki.org/wiki/API:Parsing_wikitext), [schema TOCData](https://www.mediawiki.org/wiki/API:Parsing_wikitext/TOCData), [API etiquette](https://www.mediawiki.org/wiki/API:Etiquette), [User-Agent policy](https://foundation.wikimedia.org/wiki/Policy:User-Agent_policy). `prop=sections` foi depreciado; o adaptador usa `tocdata`.

## Verificação das fontes em 15/10/2026

Os três índices em português responderam pela API oficial. A ordem esperada é 17, 18 e 22 capítulos. A variação de caminho de Azkaban mencionada acima foi conferida no próprio link do índice. No guia inglês, todas as 57 páginas retornaram uma seção `Synopsis` de nível 2; isso verifica a estrutura, não uma revisão semântica integral do conteúdo.

O adaptador implementado foi executado contra os três índices reais e as sinopses do primeiro e último capítulo de cada obra, sem chamada ao Groq. Os seis resultados foram coletados em português e tiveram hashes e revisões registrados. Revisões dos índices: Pedra `421803`, Câmara `463844`, Azkaban `482957`. Revisões dos capítulos inicial/final: `421804`/`493410`, `423658`/`436986` e `487212`/`445230`, respectivamente. Os textos normalizados dessas amostras ocuparam entre 1.425 e 5.733 bytes; isso não estima o consumo do modelo no livro inteiro.

As versões inglesas usadas quando necessário são [Philosopher's Stone](https://en.wikibooks.org/wiki/Muggles%27_Guide_to_Harry_Potter/Books/Philosopher%27s_Stone), [Chamber of Secrets](https://en.wikibooks.org/wiki/Muggles%27_Guide_to_Harry_Potter/Books/Chamber_of_Secrets) e [Prisoner of Azkaban](https://en.wikibooks.org/wiki/Muggles%27_Guide_to_Harry_Potter/Books/Prisoner_of_Azkaban). Uma consulta isolada teve timeout na investigação e foi retomada explicitamente, reforçando a necessidade de preservar progresso e apresentar falhas sem fabricar conteúdo.

Na validação de navegador, o fluxo coletou as 17 sinopses de Pedra Filosofal com APIs reais, entregou-as a um provedor IA fictício, processou 17 unidades e validou/importou o pacote controlado como rascunho (17 unidades, 13 entidades). Isso verifica a integração, não a qualidade editorial dessas entidades fictícias nem o consumo real do Groq. Também foram conferidos cancelamento em Câmara Secreta e layout a 360 px nos temas claro/escuro, sem overflow horizontal. Os testes automatizados totalizam 315 casos de backend, incluindo provedor HTTP falso, posse, CSRF, cooldown, expiração e mudança da edição durante a transferência; 40 testes de PWA/segurança, lint e build também passaram. Não houve chamada ao Groq nesta avaliação da coleta.

A jornada de navegador usou o build de produção do Next.js em `3019`, a API isolada em `5198` e um proxy de QA em `127.0.0.1:3018`, com `/api` encaminhado para essa API. O banco e a conta de teste eram descartáveis. Esse proxy não integra a implantação; os testes de endpoints cobrem Admin, antiforgery e `no-store`.

Em 16/10/2026, o estimador de entrada do provedor foi executado com a primeira sinopse real de cada obra, instruções/schema atuais e registro conhecido vazio: Pedra Filosofal, 3.014 tokens; Câmara Secreta, 2.454; Azkaban, 2.553. Todos cabem no teto configurado de 4.000 tokens estimados para essa primeira etapa. Não houve envio ao Groq; os números não são medições do tokenizer ou do uso faturado. Capítulos maiores e o crescimento do registro podem interromper etapas posteriores. O limite não é elevado nem o conteúdo truncado automaticamente: o resultado anterior permanece disponível como parcial.

## Inclusão de Ordem da Fênix em 16/10/2026

Um uso real tentou coletar o quinto volume, que não fazia parte dos três livros iniciais. A recusa não estava relacionada à chave Groq. A correção acrescenta a obra e apresenta a lista de suporte antes do clique, com uma única allowlist usada na identificação, no coletor e nos créditos públicos.

A API portuguesa retornou `missingtitle` para a página índice da obra. O [índice inglês conferido na revisão 4409182](https://en.wikibooks.org/w/index.php?title=Muggles%27%20Guide%20to%20Harry%20Potter%2FBooks%2FOrder%20of%20the%20Phoenix&oldid=4409182#Chapters), página 34534, fornece 38 links consecutivos. As sinopses inglesas do primeiro e último capítulo foram verificadas nas revisões `4655415` e `3313518`, com 1.523 e 4.831 bytes normalizados. As páginas portuguesas desses capítulos existem, mas não substituem um índice português ausente: o coletor segue o índice inglês confirmado. Uma consulta recebeu `429` e só foi repetida explicitamente após a pausa; não houve chamada ao Groq.

A jornada completa de coleta no navegador, com o mesmo ambiente isolado descrito acima, confirmou **38/38 sinopses** pelo adaptador real. A primeira execução rápida parou no limite interno de 30 operações/minuto; depois de ajustar o intervalo abortável, uma nova coleta concluiu os 38 capítulos sem erro ou retry. Não foram gerados nem importados dados desse livro na avaliação. As regressões somam 337 testes de backend, além de lint e build aprovados.
