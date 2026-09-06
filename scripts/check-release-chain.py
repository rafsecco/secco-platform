#!/usr/bin/env python3
"""Confere a cadeia de release de um pacote antes de empurrar a tag (ADR-0011).

O MinVer versiona cada pacote pela PROPRIA tag. Um `ProjectReference` para outro
projeto publicavel entra no .nupkg com a versao que o MinVer calcula para aquela
dependencia NAQUELE commit - e se a ultima tag da dependencia nao estiver no commit
sendo empacotado, ela resolve como pre-release e o Pack falha com NU5104
("a stable release should not have a prerelease dependency").

O NU5104 nao diz qual tag falta. Este script diz, antes de empurrar.

Uso:
    python scripts/check-release-chain.py sdk-logging/v    # GUARDA: sai 1 se a cadeia falha
    python scripts/check-release-chain.py Secco.SDK.Logging Secco.Templates
    python scripts/check-release-chain.py                  # levantamento: so informa, sai 0
    python scripts/check-release-chain.py --pendentes      # VIGIA: sai 1 se ha fonte por publicar

Com alvo, o script responde a pergunta que interessa antes de um release: "vou taguear
este pacote NESTE commit — o que mais precisa de tag aqui?". Aceita varios alvos; um alvo
que nao resolve e erro, nunca omissao silenciosa.

Sem alvo, ele apenas mostra o estado de todos os publicaveis e nunca falha: fora de um
release, um pacote com a tag alguns commits atras e a situacao normal, nao um problema.

`--pendentes` cobre o buraco inverso, que e o que de fato machuca o adotante: nao "tag
faltando na cadeia", e sim CODIGO ENTREGUE E NAO PUBLICADO. A diferenca importa porque
distancia global de commit e ruido - o sinal e commit que tocou o DIRETORIO do projeto
desde a ultima tag dele. Foi essa a falha que deixou o Secco.SDK.Logging 0.1.0 no feed
chamando um metodo que o Secco.LogStream.Client 0.3.0 ja havia renomeado.
"""

import subprocess
import sys
import xml.etree.ElementTree as ElementTree
from pathlib import Path

# ElementTree da stdlib e suficiente aqui: o unico XML lido sao os .csproj DESTE
# repositorio, versionados e revisados. Nao ha input externo, entao os vetores de XXE
# e billion-laughs nao se aplicam - e puxar defusedxml so para isto seria dependencia
# nova sem risco a mitigar (ADR-0020 pede avaliar dependencia nova, nao adiciona-la).

ROOT = Path(__file__).resolve().parent.parent
WORKFLOW = ROOT / ".github" / "workflows" / "publish-packages.yml"

OK = "OK "
FAIL = "FALHA"


def git(*args: str) -> str:
    result = subprocess.run(
        ["git", *args], cwd=ROOT, capture_output=True, text=True, check=False
    )
    return result.stdout.strip()


def load_projects() -> dict:
    """Todo .csproj do repositorio: caminho resolvido -> metadados e referencias.

    A fonte da verdade e o proprio csproj - `IsPackable` e `MinVerTagPrefix`. Assim
    um pacote novo nao exige tocar neste script, e o script consegue apontar um
    publicavel que ficou de fora do workflow.
    """
    projects = {}

    for csproj in ROOT.rglob("*.csproj"):
        if any(part in ("bin", "obj") for part in csproj.parts):
            continue

        try:
            root = ElementTree.parse(csproj).getroot()
        except ElementTree.ParseError:
            continue

        packable = any(
            (element.text or "").strip().lower() == "true"
            for element in root.iter("IsPackable")
        )
        prefix = next(
            ((element.text or "").strip() for element in root.iter("MinVerTagPrefix")),
            None,
        )
        references = []

        for element in root.iter("ProjectReference"):
            include = element.get("Include")
            if include:
                references.append(
                    (csproj.parent / include.replace("\\", "/")).resolve()
                )

        projects[csproj.resolve()] = {
            "name": csproj.stem,
            "packable": packable,
            "prefix": prefix,
            "references": references,
        }

    return projects


def latest_tag(prefix: str) -> str | None:
    tags = git("tag", "--list", f"{prefix}*", "--sort=-v:refname")

    return tags.split("\n")[0] if tags else None


def commits_ahead(tag: str) -> int:
    count = git("rev-list", "--count", f"{tag}..HEAD")

    return int(count) if count.isdigit() else 0


def source_commits_since(tag: str, project: Path) -> int:
    """Commits que tocaram o DIRETORIO do projeto desde a tag.

    Distancia global de commit nao serve para detectar pendencia: um pacote fica dezenas
    de commits atras do HEAD sem ter tido uma linha alterada. O que indica release
    pendente e commit no proprio projeto.
    """
    directory = project.parent.relative_to(ROOT).as_posix()
    count = git("rev-list", "--count", f"{tag}..HEAD", "--", directory)

    return int(count) if count.isdigit() else 0


def pending_packages(projects: dict) -> list:
    """Publicaveis com fonte alterada desde a propria tag, por nome."""
    pending = []

    for path, info in sorted(projects.items(), key=lambda item: item[1]["name"]):
        if not info["packable"] or not info["prefix"]:
            continue

        tag = latest_tag(info["prefix"])

        if tag is None:
            pending.append((info["name"], None, 0))
            continue

        changed = source_commits_since(tag, path)

        if changed:
            pending.append((info["name"], tag, changed))

    return pending


def registered_in_workflow(prefix: str) -> bool:
    if not WORKFLOW.exists():
        return True

    text = WORKFLOW.read_text(encoding="utf-8")

    return f'"{prefix}*"' in text and f"{prefix}*)" in text


def resolve_target(projects: dict, wanted: str) -> Path | None:
    for path, info in projects.items():
        if not info["packable"]:
            continue
        if wanted in (info["prefix"], info["name"], f"{info['name']}.csproj"):
            return path

    return None


def walk_dependencies(projects: dict, target: Path) -> list:
    """Dependencias publicaveis alcancaveis a partir do alvo, em profundidade.

    A travessia passa tambem por projetos NAO publicaveis: um Domain no meio do
    caminho pode levar a um pacote publicavel mais abaixo.
    """
    found = []
    visited = set()

    def walk(path: Path, depth: int) -> None:
        for reference in projects.get(path, {}).get("references", []):
            if reference in visited:
                continue

            visited.add(reference)
            info = projects.get(reference)

            if info is None:
                continue

            if info["packable"]:
                found.append((depth, reference, info))

            walk(reference, depth + 1)

    walk(target, 0)

    return found


def report(projects: dict, target: Path) -> bool:
    info = projects[target]
    healthy = True

    own_tag = latest_tag(info["prefix"]) if info["prefix"] else None

    if own_tag:
        changed = source_commits_since(own_tag, target)
        pendencia = (
            f", {changed} commit(s) de fonte por publicar" if changed else ", nada por publicar"
        )
        own_state = (
            f"tag mais recente: {own_tag} ({commits_ahead(own_tag)} commits atras{pendencia})"
        )
    else:
        own_state = "sem tag ainda (primeira publicacao)"

    print(f"\n{info['name']}  [{info['prefix']}]  - {own_state}")

    if info["prefix"] and not registered_in_workflow(info["prefix"]):
        healthy = False
        print(
            f"  {FAIL}  o prefixo '{info['prefix']}' NAO esta registrado em "
            f"publish-packages.yml (on.push.tags e/ou o case) - a tag nao dispara publicacao"
        )

    dependencies = walk_dependencies(projects, target)

    if not dependencies:
        print("  sem dependencia publicavel - nada a conferir")
        return healthy

    for depth, _, dependency in dependencies:
        indent = "  " + ("  " * depth)
        tag = latest_tag(dependency["prefix"]) if dependency["prefix"] else None

        if tag is None:
            healthy = False
            print(
                f"{indent}{FAIL}  {dependency['name']} - nenhuma tag "
                f"'{dependency['prefix']}*' existe; publique a dependencia primeiro"
            )
            continue

        ahead = commits_ahead(tag)

        if ahead == 0:
            print(f"{indent}{OK}  {dependency['name']} - {tag} esta no HEAD")
        else:
            healthy = False
            print(
                f"{indent}{FAIL}  {dependency['name']} - {tag} esta {ahead} commits atras. "
                f"O MinVer vai resolve-la como pre-release e o Pack falha com NU5104. "
                f"Crie uma tag '{dependency['prefix']}<versao>' neste commit e publique-a primeiro"
            )

    return healthy


def main() -> int:
    projects = load_projects()
    packable = {path: info for path, info in projects.items() if info["packable"]}

    if not packable:
        print("Nenhum projeto publicavel encontrado.", file=sys.stderr)
        return 1

    arguments = sys.argv[1:]

    if arguments == ["--pendentes"]:
        return report_pending(projects)

    if arguments:
        targets = []
        unknown = []

        for wanted in arguments:
            target = resolve_target(projects, wanted)

            if target is None:
                unknown.append(wanted)
            elif target not in targets:
                targets.append(target)

        # Alvo que nao resolve e erro, e nao omissao: um guarda que ignora em silencio o
        # que nao entendeu passa a dar verde sobre pacote nenhum.
        if unknown:
            print(
                f"Alvo(s) sem projeto publicavel correspondente: {', '.join(unknown)}. "
                f"Prefixos conhecidos: "
                f"{', '.join(sorted(i['prefix'] or '(sem prefixo)' for i in packable.values()))}",
                file=sys.stderr,
            )
            return 1
    else:
        targets = sorted(packable, key=lambda path: projects[path]["name"])

    targeted = bool(arguments)
    healthy = all([report(projects, target) for target in targets])

    print()

    if not targeted:
        pending = pending_packages(projects)

        if pending:
            print("Com fonte por publicar:")
            for name, tag, changed in pending:
                origem = f"desde {tag}" if tag else "nunca publicado"
                print(f"  - {name} ({origem}, {changed} commit(s))")
            print()

        print(
            "Levantamento apenas - nenhum veredito. Para checar antes de um release, "
            "passe o(s) alvo(s): python scripts/check-release-chain.py <prefixo-da-tag>"
        )
        return 0

    if healthy:
        print(f"Cadeia de release integra ({len(targets)} pacote(s) conferido(s)).")
        return 0

    print(
        "Cadeia de release INCOMPLETA - veja as linhas FALHA acima. "
        "Publique as dependencias primeiro, tagueando-as NESTE commit (ADR-0011)."
    )
    return 1


def report_pending(projects: dict) -> int:
    """Modo vigia: lista o que esta entregue e nao publicado, e reprova se houver algo.

    Existe para rodar agendado. O atraso entre merge e release e normal por alguns dias -
    o que nao e normal e ele ser esquecido, que foi como o Secco.SDK.Logging 0.1.0 ficou
    no feed incompativel com o client publicado ao lado dele.
    """
    pending = pending_packages(projects)

    if not pending:
        print("Nenhum pacote com fonte por publicar.")
        return 0

    print("Pacotes com codigo entregue e NAO publicado:")
    print()

    for name, tag, changed in pending:
        origem = f"ultima tag {tag}" if tag else "nunca publicado"
        print(f"  {name}  -  {origem}, {changed} commit(s) de fonte depois dela")

    print()
    print(
        "Publicar exige tag NESTE commit para cada dependencia publicavel (ADR-0011). "
        "Rode 'python scripts/check-release-chain.py <alvo>' para ver a cadeia de cada um."
    )
    return 1


if __name__ == "__main__":
    sys.exit(main())
