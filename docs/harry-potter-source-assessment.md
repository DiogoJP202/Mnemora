# Levantamento inicial — três primeiros livros de Harry Potter

Verificação em 15/10/2026 para orientar as fases 24–28. Prioridade indicada pelo usuário: **Harry Potter e a Pedra Filosofal**, **Harry Potter e a Câmara Secreta** e **Harry Potter e o Prisioneiro de Azkaban**. Este levantamento não é um pacote editorial nem comprova cobertura completa de capítulos ou lore. Não foram copiados capítulos para o repositório ou consumida IA nesta avaliação.

## Metadados disponíveis

Consultas públicas à busca da Open Library por título, seguidas da consulta JSON das edições retornadas. As respostas podem mudar; os identificadores abaixo registram a amostra observada, não uma escolha definitiva da edição do usuário.

| Livro | Obra Open Library | Edição consultada | ISBN-13 | Editora / data informada | Sumário nessa edição |
| --- | --- | --- | --- | --- | --- |
| Pedra Filosofal | `OL82563W` | [OL32172820M](https://openlibrary.org/books/OL32172820M.json) | `9789722365598` | Editorial Presença / 2022 | `table_of_contents` ausente ou vazio |
| Câmara Secreta | `OL82537W` | [OL35372744M](https://openlibrary.org/books/OL35372744M.json) | `9788532530790` | Rocco / 2017 | `table_of_contents` ausente ou vazio |
| Prisioneiro de Azkaban | `OL82536W` | [OL60429068M](https://openlibrary.org/books/OL60429068M.json) | `9788532530806` | Rocco / `19/08/2017` | `table_of_contents` ausente ou vazio |

As três respostas informaram idioma `por`. Isso sozinho não diferencia português brasileiro e português de Portugal: o primeiro resultado é da Editorial Presença. A busca também retornou uma obra diferente para Câmara Secreta e cadastros duplicados para Prisioneiro de Azkaban. A correspondência deve considerar obra, título, autoria, ISBN, editora e tradução; posição no resultado não é prova de edição correta. A data de Azkaban veio em formato não padronizado e pode não ser normalizada pelo provider atual.

As páginas da editora brasileira também mostram diferenças concretas: [Pedra Filosofal original](https://rocco.com.br/produto/harry-potter-e-a-pedra-filosofal/) e [edição comemorativa de 25 anos](https://rocco.com.br/produto/harry-potter-e-a-pedra-filosofal-edicao-comemorativa-de-25-anos/) possuem ISBNs e paginações diferentes. A página [Câmara Secreta](https://rocco.com.br/produto/harry-potter-e-a-camara-secreta/) consultada corresponde à edição ilustrada MinaLima. Uma sinopse ou título coincidente não permite substituir automaticamente a edição escolhida.

## Fontes narrativas e lacunas

| Fonte consultada | O que foi encontrado | Uso possível / limite |
| --- | --- | --- |
| [Rocco — Lendo Harry Potter](https://rocco.com.br/lendoharrypotter/) | Divulgação dos primeiros capítulos de Pedra Filosofal e Câmara Secreta | Amostra parcial; conferir edição, tradução e condições de uso antes de uma integração. Não fornece todos os capítulos. |
| [Rocco — Câmara Secreta, primeiro capítulo](https://rocco.com.br/lendoharrypotter/harry-potter-e-a-camara-secreta) | Página com capítulo identificado e texto/áudio | Pode orientar a avaliação de uma fonte delimitada, sem reproduzir o capítulo no Git ou supor autorização irrestrita. |
| [HarryPotter.com — anúncio das leituras](https://www.harrypotter.com/news/introducing-harry-potter-at-home-readings) | Campanha histórica de leitura do primeiro livro | Anúncio histórico não garante acesso atual completo, API pública ou licença de reprodução. |
| [HarryPotter.com — Chapter Challenge](https://www.harrypotter.com/features/harry-potter-philosophers-stone-chapter-challenge-twelve-the-mirror-of-erised) | Artigo associado a capítulo do primeiro livro | Conteúdo editorial pode discutir acontecimentos posteriores; associação a capítulo não comprova segurança de todas as afirmações. |
| Páginas de produtos da Rocco | Identificação e sinopses gerais | Metadados ajudam a conferir edição; sinopse geral não vira fatos com capítulo de revelação estimado. |
| Harry Potter Wiki / Fandom | Acesso não permitido pela ferramenta de consulta nesta avaliação | Não adotada como fonte; não houve tentativa de contornar o bloqueio nem cobertura verificada. |

Não foi verificada uma fonte pública, completa, permitida e segmentada por capítulo para as três edições em português. Isso não afirma que ela inexiste: registra o limite da amostra. Os três sumários vazios também não provam ausência em todas as outras edições da obra.

## Consequências para a implementação

1. **Fase 23 entregue:** o Admin pesquisa os títulos ou ISBNs, revisa metadados e importa/reutiliza o livro, sem cadastrar título e autoria à mão. A importação não cria capítulos, entidades, vínculo pessoal ou publicação.
2. **Fase 24:** devolver candidatos a fontes com procedência, idioma, edição e cobertura, destacando ambiguidade e ausência de sumário. Não prometer pacote completo ao importar esses livros.
3. **Fase 25:** aceitar somente fontes com unidade e correspondência verificadas; preservar lacunas. Dados gerais e artigos retrospectivos continuam fora do contexto de extração inicial.
4. **Avaliação editorial:** comparar cada prefixo conhecido, suspeitas, aliases e revelações posteriores. Citações textuais comprovam origem, mas não garantem veracidade nem ausência de spoiler.

O caminho atual de geração com fontes fornecidas pelo Admin permanece disponível para exceções. A automação futura deve reduzir trabalho sem inventar o que não foi encontrado, e manter revisão e publicação manuais.
