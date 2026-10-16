# Descoberta de fontes públicas — fase 24

No detalhe de um livro compartilhado em `/admin/books/{id}`, use **Encontrar referências públicas → Descobrir fontes públicas**. Salve metadados pendentes antes de consultar. O backend usa a identidade cadastrada, sem receber URLs, índice, prompts ou identificadores escolhidos pelo navegador. Não exige nova chave e não chama o Groq.

## O que a consulta faz

1. Para edição exata Open Library `OL…M`, obtém o JSON dessa edição, confere o identificador e, quando existe no cadastro, o ISBN canônico. Reaproveita o parser de sumários existente, limitado a 200 itens, sem importar unidades. Não substitui edição Google Books ou identificador legado de obra por outra edição.
2. Pesquisa o título em português ou inglês no Wikidata, em uma amostra de até três resultados. Obtém os tipos, identificadores bibliográficos, títulos e links de idioma desses resultados em uma segunda chamada. Descrições e afirmações narrativas não entram na resposta ao Admin.
3. Mantém apenas os tipos literários suportados: `Q7725634`, `Q571`, `Q8261`, `Q47461344` e `Q49084`. Não resolve toda a hierarquia de subclasses: tipos não reconhecidos, incluindo possíveis livros legítimos, são descartados com diagnóstico. Filme/jogo com título igual não vira referência de livro.
4. Distingue referência com **ISBN correspondente**, **identificador da obra correspondente** (`P648` comparado à obra da edição Open Library) e **obra não confirmada**. Um vínculo da obra não comprova tradução ou edição. Todos os candidatos exigem revisão, mesmo com ISBN.
5. Descobre links `ptwiki` e `enwiki`, construídos em hosts fixos da Wikipedia; não lê o texto dessas páginas. Artigos gerais podem conter spoilers e nunca alimentam automaticamente o gerador.

Um sumário retornado é candidato, não índice completo confirmado. Sem sumário, conflito de ISBN, fonte indisponível e edição não compatível têm estados diferentes. **Cobertura narrativa permanece `NotVerified`**, inclusive quando há referências bibliográficas. Ausência na amostra não comprova inexistência de fontes.

Referências de implementação: [Open Library Books API](https://openlibrary.org/dev/docs/api/books), [Wikibase API](https://www.mediawiki.org/wiki/Wikibase/API/en) e [Wikidata Data access](https://www.wikidata.org/wiki/Wikidata:Data_access/en).

## Relatório e procedência

**Baixar levantamento de fontes** exporta a identidade consultada, estado do sumário, seus itens candidatos, links, atribuição, idioma, correspondência, data e SHA-256 das respostas de descoberta. O hash de um link da Wikipedia é o da resposta Wikidata que forneceu o link; não é hash do artigo, que não foi obtido. Revisões de artigos e hash de conteúdo narrativo ficarão para a coleta futura.

Este JSON é material de pesquisa, não um novo memory pack: não é aceito como entrada do importador v1. Não é gravado como lore. O navegador mantém o resultado apenas em memória até o download ou a navegação. A edição salva de título/autoria reinicia o painel para evitar associar resultados antigos à nova identidade.

## API, limites e segurança

`POST /api/admin/books/{bookId}/source-discovery`, sem corpo obrigatório. Exige Admin e antiforgery; livro privado ou inexistente retorna `404`. Campos extras no corpo não controlam a consulta. A identidade do banco é conferida novamente antes da entrega; alteração durante a operação retorna `409`. O endpoint não escreve no banco e não mantém transação SQLite durante HTTP.

- Até três requisições externas por descoberta sem cache: uma edição Open Library, uma busca Wikidata e uma leitura em lote Wikidata.
- HTTPS em dois hosts fixos, parâmetros escapados, IDs limitados por padrão, sem seguir redirects, sem navegar para URLs retornados e sem executar instruções de fontes.
- Até 1 MiB por resposta, conferido pelo header e durante leitura, profundidade JSON 64 e timeout de oito segundos por chamada, incluindo leitura do corpo. Prazo total de 25 segundos.
- Uma descoberta global ativa por processo. Concorrência recebe `429` com `Retry-After: 15`; não há fila nem repetição automática. Usa a política existente `import-external`: dez operações por minuto por Admin, compartilhadas com importações.
- Cache em memória por identidade bibliográfica: uma hora; uma falha de fonte reduz a retenção a um minuto. Cada relatório ocupa dez unidades do cache global de mil, limitando-o a no máximo cem relatórios quando não há outros itens. O cache guarda projeções públicas e hashes, não JSON bruto de todas as fontes ou versões ilimitadas.
- Cancelamento do cliente libera a operação depois de terminar/cancelar suas chamadas. Erro, JSON inválido, limite, timeout ou `429` de uma fonte preservam o que a outra conseguiu obter e geram diagnóstico sem detalhes internos. Não há fallback pago.
- Respostas HTTP autenticadas usam `Cache-Control: no-store`. `/api`, `/admin` e `/app` permanecem fora do service worker; cache público no backend não é cache privado no navegador.

Não existe migration nova, worker ou persistência de pesquisa nesta fase. Reinício perde o cache; relatórios baixados podem ser conservados pelo Admin. A publicação continua manual, e as regras antisspoiler do leitor não foram alteradas.

## Próxima etapa

A fase 25 acrescentou [coleta permitida e delimitação por capítulo](automatic-sources.md), inicialmente com o guia Wikibooks dos três primeiros Harry Potter. Links gerais da descoberta bibliográfica continuam sem equivaler a fontes aprovadas: o novo coletor usa destinos e seções específicos, com revisões e procedência. A entrada manual permanece para outras obras e exceções.
