# Implantação no Render

O Mnemora pode ser publicado como um único Web Service Docker no Render. O container executa o Next.js na porta pública fornecida pelo Render e a API ASP.NET Core em `127.0.0.1:5100`; o rewrite `/api/*` mantém frontend e API na mesma origem.

O [`render.yaml`](../render.yaml) cria, por padrão, uma **demonstração gratuita e sem cartão** com:

- região `virginia` e plano `free`;
- uma única instância;
- health check público em `/api/health`;
- deploy automático somente depois que os checks do commit passam;
- SQLite e chaves do ASP.NET Core Data Protection no filesystem efêmero do container;
- seed do pack original, sem conta Admin inicial.

## Limites da demonstração gratuita

Use essa implantação apenas para conhecer o produto. O serviço gratuito entra em repouso depois de 15 minutos sem acessos e pode levar cerca de um minuto para responder novamente durante a partida a frio. Um aviso visível identifica esse ambiente na interface.

O filesystem do serviço não é persistente. O banco SQLite, as contas cadastradas, as sessões, o progresso, as notas e as revisões podem desaparecer quando a instância entra em repouso, reinicia ou recebe um novo deploy. O seed recria somente o pack demonstrativo na próxima inicialização. Não armazene dados importantes nessa modalidade.

O Blueprint também não solicita `ADMIN_EMAIL` nem `ADMIN_PASSWORD`. A demonstração pública permite cadastrar leitores e testar a experiência de leitura, mas não fornece acesso inicial à área `/admin`.

## Criar a demonstração pelo Blueprint

1. Abra o [Deploy to Render do Mnemora](https://render.com/deploy?repo=https://github.com/DiogoJP202/Mnemora) ou, no Dashboard, escolha **New > Blueprint**.
2. Conecte a conta GitHub e selecione o repositório do Mnemora.
3. Confirme que o Render encontrou o `render.yaml` na raiz e mostra o plano `free`, sem disco.
4. Aplique o Blueprint; ele não deve solicitar cartão nem credenciais de Admin.
5. Aguarde o build, o início do container e o health check de `/api/health`.
6. Abra a URL `https://<nome-do-serviço>.onrender.com`, cadastre um leitor e experimente o pack demonstrativo.

O entrypoint usa `RENDER_EXTERNAL_URL` para configurar a origem web aceita pela API. Não é necessário antecipar a URL gerada. Na inicialização, a API aplica as migrations e executa o seed idempotente.

Se você adicionar um domínio próprio, configure `Mnemora__WebOrigin` com a origem HTTPS exata desse domínio e faça um novo deploy. Para habilitar a busca do Google Books, adicione `GOOGLE_BOOKS_API_KEY` como secret em **Environment** e faça outro deploy.

## Validar a implantação

Depois que o serviço ficar disponível:

1. abra `/api/health` e confirme a resposta `{"status":"ok"}`;
2. abra a landing page e verifique o manifesto da PWA;
3. cadastre um leitor e percorra biblioteca, progresso, Recall, notas e revisão;
4. avance e retroceda o progresso para confirmar que o lore posterior volta a ficar oculto;
5. aguarde ou reinicie o serviço apenas se quiser confirmar que a demo recupera o pack e descarta os dados efêmeros.

O health check passa pelo Next.js e pelo rewrite até a API, então verifica os dois processos do container. Respostas privadas usam `Cache-Control: no-store`, e o service worker não armazena `/api`, `/app` nem `/admin`.

## Opção paga e durável

Para preservar contas, progresso e sessões, altere o serviço para o plano `0.5c-512mb`, anexe um disco de 1 GB em `/var/data` e mantenha o SQLite em `/var/data/mnemora.db` e as chaves em `/var/data/keys`. Você também pode adicionar `ADMIN_EMAIL` e `ADMIN_PASSWORD` como secrets para provisionar o Admin inicial.

Com os preços atuais do Render, essa configuração custa cerca de **US$ 7,25 por mês**: US$ 7,00 pelo serviço e US$ 0,25 pelo disco de 1 GB. O valor não inclui eventual uso adicional e pode mudar; confirme-o na página de preços antes de contratar.

SQLite com disco persistente exige uma única instância e não oferece failover. Para escalar horizontalmente, migre o `MnemoraDbContext` para um banco gerenciado, remova a dependência do arquivo local e valide novamente migrations, concorrência e proteção de dados.

## Atualizar ou remover

Para atualizar, envie um commit aprovado para `main` ou escolha **Manual Deploy** no Dashboard. No plano gratuito, qualquer deploy pode recriar o banco do zero. Para remover a demonstração, exclua o Web Service no Dashboard; não há dados persistentes a preservar.

Consulte também a documentação oficial do Render sobre [serviços gratuitos](https://render.com/docs/free), [especificação de Blueprints](https://render.com/docs/blueprint-spec), [discos persistentes](https://render.com/docs/disks) e [preços](https://render.com/pricing).
