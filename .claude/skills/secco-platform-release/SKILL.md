---
name: secco-platform-release
description: Processo de publicação de pacotes NuGet da Secco Platform via tag git (ADR-0011/0014, workflow `.github/workflows/publish-packages.yml`). Usar SEMPRE que a tarefa for publicar/versionar um pacote (Secco.SharedKernel, Secco.SDK.AspNetCore, Secco.SDK.EntityFrameworkCore, Secco.SDK.Testing, Secco.LogStream.Client, Secco.SecureGate.Client, Secco.NotificationHub.Client, Secco.Templates, ou um novo pacote publicável), criar/empurrar uma tag de release, ou quando o usuário mencionar "publicar pacote", "release", "tag de versão", "NuGet", "MinVer", "pacote amadureceu" ou o workflow `publish-packages.yml`.
---

# Secco Platform — Publicação de pacotes NuGet

Publicação é disparada por **tag git**: criar a tag certa = publicar o pacote (ADR-0011/0014). O MinVer calcula a versão a partir da tag; não há passo manual de "definir versão" em nenhum csproj.

## 1. Pré-requisito no csproj do pacote

Antes da primeira publicação de um projeto, confirmar que o csproj tem:

- `<IsPackable>true</IsPackable>`
- `<MinVerTagPrefix>` — precisa bater **exatamente** com o prefixo usado no padrão de tag do workflow (ver tabela abaixo).
- `<Description>` e `<PackageTags>` preenchidos.
- README empacotado: `<None Include="README.md" Pack="true" PackagePath="\" />`.

Pacote sem `IsPackable=true` não gera `.nupkg` mesmo com a tag certa — o Pack roda mas não produz artefato.

## 2. Pacote novo: três lugares a atualizar

Um projeto publicável novo precisa entrar em **três lugares** de `.github/workflows/publish-packages.yml`, todos no mesmo PR:

1. A lista `on.push.tags` (padrão glob da tag, ex. `"meupacote/v*"`).
2. O script de mapeamento tag→projeto (bloco `case ... esac`, associando o padrão ao caminho do `.csproj`).
3. A tabela de referência desta skill (seção 4), para não precisar abrir o YAML da próxima vez.

## 3. Formato de tag e push

Formato: `<prefixo>/v<semver>` — ex. `sharedkernel/v0.3.0`.

**Empurrar tags uma de cada vez**: `git push origin <tag>` por tag, nunca várias tags no mesmo `git push`. Confirmado empiricamente nesta plataforma: o GitHub Actions **não dispara o evento de tag-push (`create`) para mais de 3 tags empurradas juntas** no mesmo comando — falha silenciosa, sem erro no push, o workflow simplesmente não roda para as tags excedentes. Publicando vários pacotes de uma vez, iterar o push tag por tag (loop), não montar `git push origin tag1 tag2 tag3 tag4 tag5`.

**Reincidiu em 2026-09-06**, numa levada de seis tags empurradas num comando só — escolhido, ironicamente, para evitar uma corrida entre os workflows. As seis tags entraram no remoto, **zero** workflows rodaram, e nada avisou. A regra acima já estava escrita aqui; o que faltou foi ler a skill antes de publicar. Se você chegou até aqui só depois do push, veja a seção 6.

### Dependência entre pacotes: tag estável exige tag estável da dependência no mesmo commit

**Não faça essa conferência de cabeça — rode o script:**

```bash
python scripts/check-release-chain.py <prefixo-da-tag>/v    # ex.: sdk-logging/v
```

Ele lê os `.csproj` (a fonte da verdade: `IsPackable` + `MinVerTagPrefix`), percorre os `ProjectReference` **transitivamente** e diz, para cada dependência publicável, se a tag dela está neste commit ou quantos commits atrás está. Sai com 1 se faltar alguma, e também acusa pacote publicável que não esteja registrado no `publish-packages.yml`. Aceita **vários alvos** no mesmo comando; alvo que não resolve é erro, nunca omissão silenciosa. Sem alvo, faz apenas um levantamento de todos os pacotes e nunca falha.

O terceiro modo responde à pergunta inversa — **existe código entregue que nunca virou pacote?**:

```bash
python scripts/check-release-chain.py --pendentes    # sai 1 se houver fonte por publicar
```

A diferença entre os dois é o que se mede: a guarda olha distância de commit até o HEAD (que é ruído — um pacote fica dezenas de commits atrás sem ter mudado uma linha); o vigia olha commit que tocou o **diretório do projeto** desde a tag dele. É o modo que o `.github/workflows/release-pendente.yml` roda toda segunda-feira, abrindo (e fechando sozinha) uma issue rotulada `release-pendente`.

O mesmo script roda como **guarda no workflow**, antes do Pack: a tag que não tem cadeia íntegra falha cedo, com a mensagem dizendo qual tag criar, em vez de terminar em `NU5104` — que não diz.


O MinVer versiona cada pacote pela **própria** tag: um `ProjectReference` para outro pacote publicável (ex.: SDK → SharedKernel) entra no `.nupkg` com a versão que o MinVer calcula para a dependência **naquele commit**. Se a última tag da dependência não estiver no commit sendo empacotado (height > 0), a dependência resolve como pré-release (`0.3.1-alpha.0.N`) e o Pack falha com NU5104 — "a stable release should not have a prerelease dependency" (warnings = erros).

Confirmado empiricamente: `sdk/v0.4.0` falhou no Pack porque a última tag do SharedKernel (`sharedkernel/v0.3.0`) estava 24 commits atrás. Correção: taguear a dependência no **mesmo commit** (patch bump se ela não mudou de forma relevante), publicar a dependência primeiro e então (re)executar o publish do dependente. Ao liberar qualquer pacote com `ProjectReference` publicável, conferir isso ANTES de empurrar a tag.

## 4. Tabela de referência: prefixo → projeto

Espelha o script `case` do workflow — atualizar aqui sempre que o workflow mudar.

| Prefixo da tag | Projeto |
|---|---|
| `sharedkernel/v*` | `src/SharedKernel/Secco.SharedKernel/Secco.SharedKernel.csproj` |
| `sdk/v*` | `src/SDK/Secco.SDK.AspNetCore/Secco.SDK.AspNetCore.csproj` |
| `sdk-efcore/v*` | `src/SDK/Secco.SDK.EntityFrameworkCore/Secco.SDK.EntityFrameworkCore.csproj` |
| `sdk-testing/v*` | `src/SDK/Secco.SDK.Testing/Secco.SDK.Testing.csproj` |
| `sdk-logging/v*` | `src/SDK/Secco.SDK.Logging/Secco.SDK.Logging.csproj` |
| `logstream-client/v*` | `src/LogStream/Secco.LogStream.Client/Secco.LogStream.Client.csproj` |
| `notificationhub-client/v*` | `src/NotificationHub/Secco.NotificationHub.Client/Secco.NotificationHub.Client.csproj` |
| `securegate-client/v*` | `src/SecureGate/Secco.SecureGate.Client/Secco.SecureGate.Client.csproj` |
| `templates/v*` | `templates/Secco.Templates.csproj` |

## 5. Projeto fora da `Secco.Platform.slnx`

Hoje só `templates/Secco.Templates.csproj` está nesse caso — fora da `.slnx` **de propósito** (não é produto buildável, é o gerador de templates). Consequência: o step de Pack do workflow **não pode usar `--no-build`**. O `dotnet build Secco.Platform.slnx` do job nunca restaura nem builda esse projeto, então `--no-build` no Pack falha com `NETSDK1004` (assets file não encontrado). O workflow atual já reflete isso (Pack deixa o `dotnet pack` restaurar/buildar por conta própria) — este item existe para ninguém "otimizar" reintroduzindo `--no-build` e quebrar esse pacote de novo.

## 6. Verificar, não assumir

Push de tag bem-sucedido **não** significa que o publish rodou. Depois de empurrar, confirmar o workflow de fato disparou e concluiu:

```bash
gh run list --workflow=publish-packages.yml --limit 10
```

Ou checar a aba Actions no GitHub. Se uma tag não aparecer como run recente, ela provavelmente caiu na limitação da seção 3 (batch >3 tags).

**Recuperação — republicar uma tag que já existe**, sem cirurgia em tag:

```bash
gh workflow run publish-packages.yml -f tag=sdk-logging/v0.1.1
```

O `workflow_dispatch` faz checkout **da tag** (é dela que o MinVer tira a versão) e segue idêntico ao caminho do push. Serve também para reexecutar uma publicação que falhou por motivo transitório. Apagar e reempurrar a tag continua funcionando, mas é o caminho pior: durante a janela em que a tag não existe, a guarda de cadeia de qualquer dependente em execução reprova.

## 7. Versões atualmente publicadas

Esta skill documenta o **mecanismo** de release, não o estado atual dos pacotes. Para saber o que está publicado agora, ver `docs/getting-started.md` (tabela de versões) — não duplicar essa tabela aqui.
