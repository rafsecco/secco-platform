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
    python scripts/check-release-chain.py Secco.SDK.Logging
    python scripts/check-release-chain.py                  # levantamento: so informa, sai 0

Com alvo, o script responde a pergunta que interessa antes de um release: "vou taguear
este pacote NESTE commit — o que mais precisa de tag aqui?". Sem alvo, ele apenas mostra
o estado de todos os publicaveis e nunca falha: fora de um release, um pacote com a tag
alguns commits atras e a situacao normal, nao um problema.
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
    own_state = (
        f"tag mais recente: {own_tag} ({commits_ahead(own_tag)} commits atras)"
        if own_tag
        else "sem tag ainda (primeira publicacao)"
    )

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

    if len(sys.argv) > 1:
        wanted = sys.argv[1]
        target = resolve_target(projects, wanted)

        if target is None:
            print(
                f"Alvo '{wanted}' nao corresponde a nenhum projeto publicavel.\n"
                f"Prefixos conhecidos: "
                f"{', '.join(sorted(i['prefix'] or '(sem prefixo)' for i in packable.values()))}",
                file=sys.stderr,
            )
            return 1

        targets = [target]
    else:
        targets = sorted(packable, key=lambda path: projects[path]["name"])

    targeted = len(sys.argv) > 1
    healthy = all([report(projects, target) for target in targets])

    print()

    if not targeted:
        print(
            "Levantamento apenas - nenhum veredito. Para checar antes de um release, "
            "passe o alvo: python scripts/check-release-chain.py <prefixo-da-tag>"
        )
        return 0

    if healthy:
        print("Cadeia de release integra.")
        return 0

    print(
        "Cadeia de release INCOMPLETA - veja as linhas FALHA acima. "
        "Publique as dependencias primeiro, tagueando-as NESTE commit (ADR-0011)."
    )
    return 1


if __name__ == "__main__":
    sys.exit(main())
