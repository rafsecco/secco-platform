# Política de segurança

A Secco Platform publica pacotes que tratam identidade, emissão de token, autorização e connection strings de banco por tenant. Uma falha aqui não fica contida num produto — atravessa todos os adotantes. Por isso a segurança é critério de design, não revisão posterior (ADR-0020).

## Reportar uma vulnerabilidade

**Não abra issue pública** para vulnerabilidade. Use o canal privado do GitHub:

> Repositório → aba **Security** → **Report a vulnerability**

Isso abre um advisory privado, visível apenas para os mantenedores, onde a correção pode ser discutida e preparada antes de qualquer divulgação.

No relato, ajuda muito ter:

- versão do pacote ou commit afetado;
- o que um atacante consegue fazer (não só o que está errado);
- passos de reprodução, se houver;
- se você já sabe, o limite do impacto — um tenant, todos os tenants, todos os adotantes.

## Versões suportadas

A plataforma está em fase de fundação e ainda não atingiu 1.0. **Só a versão mais recente de cada pacote recebe correção**; não há backport para linhas anteriores. Quando houver 1.0 e política de suporte a versões, ela entra por ADR própria (está no backlog de ADRs).

## Escopo

Interessa qualquer coisa que quebre uma destas garantias, que são as que a plataforma promete:

- **Isolamento de tenant** (ADR-0005) — dado de um tenant alcançável a partir de outro, por qualquer caminho.
- **Emissão e validação de token** (ADR-0007, ADR-0022) — forjar token aceito por um produto, ou escalar privilégio com token legítimo.
- **Autorização** (ADR-0021) — obter permissão não concedida, ou revogação que não propaga.
- **Connection strings de tenant** (ADR-0025) — recuperar em claro o que deveria estar cifrado, ou vazar por API, log ou mensagem de erro.
- **Vazamento de informação** — stack trace, segredo ou dado de outro tenant em resposta ou log de produção.

Fora de escopo: as credenciais de desenvolvimento versionadas no repositório. Elas são conhecidas de propósito, existem só sob `IsDevelopment()` com guarda dupla (ADR-0019), e o SDK **falha o startup** se uma chave de desenvolvimento for usada em Production.

## Dependências

O repositório usa Central Package Management (ADR-0011) e pins explícitos quando uma dependência transitiva traz CVE. Esses pins **envelhecem**: uma versão fixada para escapar de uma CVE pode passar a acusar outra. A verificação faz parte da manutenção, não é decisão de uma vez só:

```bash
dotnet list Secco.Platform.slnx package --vulnerable --include-transitive
```

O build trata `NU1903` como sinal a investigar, não ruído.
