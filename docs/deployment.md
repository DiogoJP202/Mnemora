# Implantação no Render

O Mnemora pode ser publicado como um único Web Service Docker no Render. O container executa o Next.js na porta pública fornecida pelo Render e a API ASP.NET Core em `127.0.0.1:5100`; o rewrite `/api/*` preserva a mesma origem no navegador.

O arquivo [`render.yaml`](../render.yaml) declara a infraestrutura usada por essa implantação:

- região `virginia`;
- plano `0.5c-512mb`, com 0,5 CPU e 512 MB de memória;
- uma única instância;
- health check público em `/api/health`;
- deploy automático somente depois que os checks do commit passam;
- disco persistente de 1 GB montado em `/var/data`;
- SQLite em `/var/data/mnemora.db`;
- chaves do ASP.NET Core Data Protection em `/var/data/keys`, para preservar sessões entre deploys.

## Custo

Com os preços atuais do Render, a configuração base custa **US$ 7,25 por mês**:

| Recurso | Preço atual |
| --- | ---: |
| Web Service `0.5c-512mb` | US$ 7,00/mês |
| Disco persistente, 1 GB a US$ 0,25/GB/mês | US$ 0,25/mês |

Esse total não inclui uso adicional que o Render possa cobrar, como largura de banda excedente. Consulte a página de preços do Render antes de criar o serviço, pois nomes, capacidades e valores dos planos podem mudar.

## Antes de publicar

1. Faça push do código e confirme que o workflow do GitHub Actions terminou com sucesso.
2. Separe um e-mail para a conta administrativa inicial.
3. Gere uma senha exclusiva com pelo menos 10 caracteres, uma letra maiúscula, uma minúscula e um dígito.
4. Se quiser habilitar a pesquisa externa, obtenha uma chave da Google Books API. Ela é opcional e pode ser adicionada depois.

As variáveis `ADMIN_EMAIL` e `ADMIN_PASSWORD` estão marcadas com `sync: false`. O Render solicita os valores na criação do Blueprint e não os grava no repositório. Não reutilize uma senha pessoal.

## Criar o serviço pelo Blueprint

1. Abra o [Deploy to Render do Mnemora](https://render.com/deploy?repo=https://github.com/DiogoJP202/Mnemora) ou, no Dashboard, escolha **New > Blueprint**.
2. Conecte a conta GitHub e selecione o repositório do Mnemora.
3. Confirme que o Render encontrou o `render.yaml` na raiz.
4. Informe `ADMIN_EMAIL` e `ADMIN_PASSWORD` quando solicitado.
5. Revise o serviço, o plano de 0,5 CPU e 512 MB e o disco de 1 GB e aplique o Blueprint.
6. Aguarde o build, o início do container e o health check de `/api/health`.
7. Abra a URL `https://<nome-do-serviço>.onrender.com`, faça login com o Admin configurado e confirme o acesso à administração.

O entrypoint usa `RENDER_EXTERNAL_URL` para configurar a origem web aceita pela API. Não é necessário antecipar a URL gerada no Blueprint. Na inicialização, a API aplica as migrations e executa o seed idempotente; o pack demonstrativo e o Admin não são duplicados em reinícios posteriores.

Se você adicionar um domínio próprio, configure `Mnemora__WebOrigin` com a origem HTTPS exata desse domínio e faça um novo deploy. Esse valor explícito substitui a URL `onrender.com` derivada automaticamente.

Para habilitar a busca do Google Books depois do primeiro deploy, abra **Environment** no serviço, adicione `GOOGLE_BOOKS_API_KEY` como secret e faça um novo deploy.

## Validar a implantação

Depois que o serviço ficar disponível:

1. abra `/api/health` e confirme a resposta `{"status":"ok"}`;
2. abra a landing page e verifique o manifesto da PWA;
3. entre com o Admin e confirme o acesso a `/admin`;
4. crie um leitor separado e percorra biblioteca, progresso, Recall, notas e revisão;
5. avance e retroceda o progresso para confirmar que o lore posterior volta a ficar oculto;
6. reinicie o serviço pelo Dashboard e confirme que dados e sessões continuam disponíveis.

O health check passa pelo Next.js e pelo rewrite até a API, então ele verifica os dois processos do container. Respostas privadas continuam com `Cache-Control: no-store`, e o service worker não armazena `/api`, `/app` nem `/admin`.

## Operação e limitações

SQLite e o disco persistente tornam esta implantação adequada para um MVP de baixo tráfego, mas exigem **uma única instância**. O Render não permite escalar um serviço com disco persistente para várias instâncias, e o arquivo SQLite local não pode ser compartilhado com réplicas. O disco também prende o serviço à região declarada.

Deploys e reinícios podem causar uma breve indisponibilidade, pois não há uma segunda instância para receber tráfego. Esta topologia também não oferece failover do banco. Crie uma rotina de backup fora do serviço e teste a restauração antes de armazenar dados importantes.

Para escalar horizontalmente, migre o `MnemoraDbContext` para um banco gerenciado compatível, remova a dependência do disco local e valide novamente migrations, concorrência e proteção de dados. Só então aumente o número de instâncias.

Mudanças em `main` só iniciam um deploy automático quando os checks associados ao commit passam, devido a `autoDeployTrigger: checksPass`. Um deploy manual pelo Dashboard continua sendo uma ação operacional separada.

## Atualizar ou remover

Para atualizar, envie um commit aprovado para `main` ou escolha **Manual Deploy** no Dashboard. A inicialização reaplica migrations pendentes sem recriar o banco.

Antes de excluir o serviço ou o disco, faça um backup do banco. Remover o Web Service não deve ser tratado como backup: a exclusão do disco elimina o `mnemora.db` e as chaves de Data Protection persistidas.

Consulte também a documentação oficial do Render sobre a [especificação de Blueprints](https://render.com/docs/blueprint-spec), [discos persistentes](https://render.com/docs/disks) e [preços](https://render.com/pricing).
