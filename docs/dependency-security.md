# Segurança de dependências

A CI executa a auditoria completa do npm pelo gate `node scripts/audit-npm.mjs` (a partir da raiz). O gate bloqueia achados `high` e `critical`, inclusive em dependências de desenvolvimento, e preserva o lint completo. Os testes de fronteira são executados com `node --test tests/security/audit-npm.test.mjs`.

## Exceção de desenvolvimento — revisão de 2026-10-09

O advisory [GHSA-vfj7-8cjw-p6xm / CVE-2026-93687](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm) afeta `braces` até `3.0.3`: padrões de chaves profundamente aninhados podem esgotar a pilha e encerrar o processo Node. Na revisão de 2026-10-09, a fonte primária informa que não existe versão corrigida. A correção sugerida pelo npm é um downgrade de `eslint-config-next` para `14.2.35`, incompatível com a versão de Next.js adotada no projeto.

No lockfile atual, a única rota afetada é:

```text
eslint-config-next@16.3.8
  -> @next/eslint-plugin-next@16.3.8
  -> fast-glob@3.3.1
  -> micromatch@4.0.8
  -> braces@3.0.3
```

Todos os cinco pacotes têm `dev: true`. A rota processa os globs e a configuração local do lint; ela não recebe texto de leitores, buscas, uploads ou respostas de provedores. A aceitação temporária permite manter o lint da versão atual enquanto se aguarda uma correção upstream. O achado continua sendo uma vulnerabilidade conhecida; a saída do gate diz explicitamente **“exceção de desenvolvimento”**.

A exceção vence no início de **2026-11-09 UTC**. O gate falha nessa data e depois dela se o achado continuar presente. A equipe responsável pelo repositório deve revisar a disponibilidade de uma correção e atualizar a cadeia assim que houver uma versão compatível; uma extensão exige nova revisão documentada e alteração explícita do código e dos testes.

## Limites verificáveis

- Apenas a URL exata desse advisory em `braces@3.0.3` é aceita. Outro advisory, severidade `critical`, versão diferente ou nova correção disponível na cadeia exige revisão.
- O relatório precisa ser `auditReportVersion: 2`, e o lockfile precisa ser v3. Nomes, versões, nós afetados, propagação e consumidores devem corresponder à rota revisada; todos os nós precisam continuar exclusivamente em desenvolvimento. Instâncias aninhadas, novas rotas, dependências de produção, opcionais ou pares adicionais bloqueiam o gate.
- Outros achados `high`/`critical` bloqueiam a CI. Achados inferiores continuam reportados, conforme o nível `high` já adotado.
- Erros de execução/rede, JSON inválido, dados ou estruturas desconhecidas, referências ausentes e contagens ou códigos de saída inconsistentes bloqueiam a CI. A exceção não usa `--omit=dev`, downgrade, redução das regras de lint ou supressão genérica de pacote.
- A exceção só é avaliada quando o advisory aparece. Uma auditoria sem achados `high`/`critical` passa normalmente, inclusive depois do vencimento.

O gate possui testes para a cadeia permitida, cada versão/classificação de desenvolvimento, outras vulnerabilidades, mudanças de grafo, descoberta de patch, erros de auditoria e a fronteira exata de expiração.
