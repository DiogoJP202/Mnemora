# Evoluções futuras do Mnemora

Este documento registra possibilidades posteriores ao MVP. Nenhum item abaixo está implementado ou prometido na versão atual. Qualquer evolução de leitura deve preservar a regra central: selecionar o conhecimento autorizado no backend antes de buscar, ranquear, resumir ou gerar uma resposta.

## Recuperação de senha e conta

- implementar solicitação de recuperação com token de uso único e expiração curta;
- integrar um provedor de e-mail transacional e manter uma resposta neutra para não revelar contas cadastradas;
- permitir redefinição e invalidação das sessões anteriores;
- adicionar confirmação e alteração de e-mail, alteração de senha e encerramento da conta;
- oferecer segundo fator e gestão de sessões para instalações que precisem de segurança adicional.

O fluxo deve usar os tokens do ASP.NET Core Identity, limitar tentativas e nunca registrar tokens ou credenciais em logs.

## Busca semântica

- gerar embeddings para nomes, aliases, fatos e pistas curadas;
- indexar cada fragmento com livro, entidade e ponto de revelação;
- aplicar o `KnowledgeScope` antes da recuperação vetorial, inclusive em retrocessos de progresso;
- combinar busca textual e semântica com resultados curtos e explicáveis;
- manter resultado vazio quando só houver correspondência em conteúdo futuro.

A avaliação precisa incluir ataques com termos futuros, aliases ainda bloqueados e inferências indiretas. Um índice que filtre apenas depois da busca não atende à política antisspoiler.

## Assistência por IA

- sugerir resumos e pistas para revisão do Admin, sempre com aprovação humana;
- auxiliar na classificação de entidades, relações e possíveis unidades de revelação;
- responder perguntas do leitor somente a partir de um conjunto de fatos já autorizado;
- registrar versão, origem e revisão editorial do conteúdo gerado;
- medir alucinações e vazamentos antes de habilitar qualquer resposta ao leitor.

Textos completos protegidos por direitos autorais não devem ser enviados ou armazenados sem autorização. Metadados externos continuam separados do lore seguro, e uma resposta gerada nunca deve completar lacunas com conhecimento fora do escopo liberado.

## Repetição espaçada

- transformar `UserRecallActivity` em histórico para um agendador por entidade;
- calcular próxima revisão a partir de “Eu lembro” e “Preciso revisar”;
- permitir metas, frequência e pausa por livro;
- criar sessões curtas com fatos e pistas já revelados;
- adaptar o cronograma quando o leitor retroceder, removendo temporariamente conteúdo que deixou de estar visível.

O algoritmo de agendamento não pode conceder visibilidade. Ele seleciona somente entre entidades que o motor antisspoiler já autorizou naquele pedido.

## Packs de lore e trabalho editorial

- importar e exportar packs em um formato versionado e validável;
- oferecer rascunho, revisão, publicação e histórico de alterações;
- comparar previews em diferentes pontos de leitura antes de publicar;
- validar automaticamente relações, aliases e fatos que antecedam suas entidades;
- mapear edições diferentes do mesmo livro para uma sequência canônica de unidades;
- criar ferramentas de colaboração e moderação para packs comunitários.

Conteúdo público ou comunitário precisará de autoria, licença, atribuição, denúncia e trilha de auditoria.

## Biblioteca e experiência do leitor

- sincronizar alterações feitas offline e resolver conflitos de progresso entre dispositivos;
- oferecer importação e exportação dos dados pessoais;
- organizar séries, coleções e releituras independentes;
- ampliar internacionalização além de `pt-BR`;
- adicionar preferências de acessibilidade e testes automatizados mais amplos;
- permitir notificações opt-in para sessões de revisão, sem incluir spoilers no texto da notificação.

## Plataforma e operação

- migrar para PostgreSQL quando houver necessidade de concorrência e múltiplas instâncias;
- criar jobs para indexação, e-mail, validação e importações demoradas;
- adicionar métricas, tracing e alertas sem registrar notas, consultas ou conteúdo privado;
- ampliar o CI existente com testes de migração, backup/restauração e verificações periódicas;
- definir retenção, exclusão e portabilidade de dados antes de hospedar usuários reais;
- avaliar offline autenticado com armazenamento cifrado e regras explícitas de expiração, somente após uma análise de risco própria.

## Critérios para promover uma ideia ao produto

Antes de entrar no escopo, uma evolução deve:

1. definir o comportamento ao avançar, retroceder e remover o progresso;
2. provar que nenhum texto futuro chega ao cliente, cache, índice de busca, log ou provedor externo;
3. incluir testes de fronteira, autorização e isolamento entre usuários;
4. documentar migração, configuração, observabilidade e forma de reversão;
5. manter uma experiência útil quando integrações externas estiverem indisponíveis.
