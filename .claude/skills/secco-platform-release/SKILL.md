---
name: secco-platform-release
description: Processo de publicação de pacotes NuGet da Secco Platform via tag git (ADR-0011/0014, workflow `.github/workflows/publish-packages.yml`). Usar SEMPRE que a tarefa for publicar/versionar um pacote (Secco.SharedKernel, Secco.SDK.AspNetCore, Secco.SDK.EntityFrameworkCore, Secco.LogStream.Client, Secco.SecureGate.Client, Secco.Templates, ou um novo pacote publicável), criar/empurrar uma tag de release, ou quando o usuário mencionar "publicar pacote", "release", "tag de versão", "NuGet", "MinVer", "pacote amadureceu" ou o workflow `publish-packages.yml`.
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

## 4. Tabela de referência: prefixo → projeto

Espelha o script `case` do workflow — atualizar aqui sempre que o workflow mudar.

| Prefixo da tag | Projeto |
|---|---|
| `sharedkernel/v*` | `src/SharedKernel/Secco.SharedKernel/Secco.SharedKernel.csproj` |
| `sdk/v*` | `src/SDK/Secco.SDK.AspNetCore/Secco.SDK.AspNetCore.csproj` |
| `sdk-efcore/v*` | `src/SDK/Secco.SDK.EntityFrameworkCore/Secco.SDK.EntityFrameworkCore.csproj` |
| `logstream-client/v*` | `src/LogStream/Secco.LogStream.Client/Secco.LogStream.Client.csproj` |
| `securegate-client/v*` | `src/SecureGate/Secco.SecureGate.Client/Secco.SecureGate.Client.csproj` |
| `templates/v*` | `templates/Secco.Templates.csproj` |

## 5. Projeto fora da `Secco.Platform.slnx`

Hoje só `templates/Secco.Templates.csproj` está nesse caso — fora da `.slnx` **de propósito** (não é produto buildável, é o gerador de templates). Consequência: o step de Pack do workflow **não pode usar `--no-build`**. O `dotnet build Secco.Platform.slnx` do job nunca restaura nem builda esse projeto, então `--no-build` no Pack falha com `NETSDK1004` (assets file não encontrado). O workflow atual já reflete isso (Pack deixa o `dotnet pack` restaurar/buildar por conta própria) — este item existe para ninguém "otimizar" reintroduzindo `--no-build` e quebrar esse pacote de novo.

## 6. Verificar, não assumir

Push de tag bem-sucedido **não** significa que o publish rodou. Depois de empurrar, confirmar o workflow de fato disparou e concluiu:

```bash
gh run list --workflow=publish-packages.yml --limit 10
```

Ou checar a aba Actions no GitHub. Se uma tag não aparecer como run recente, ela provavelmente caiu na limitação da seção 3 (batch >3 tags) — apagar a tag remota (`git push origin --delete <tag>`) e reempurrar sozinha.

## 7. Versões atualmente publicadas

Esta skill documenta o **mecanismo** de release, não o estado atual dos pacotes. Para saber o que está publicado agora, ver `docs/getting-started.md` (tabela de versões) — não duplicar essa tabela aqui.
