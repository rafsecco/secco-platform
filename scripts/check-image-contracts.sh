#!/usr/bin/env bash
# Guarda de compatibilidade das imagens (ADR-0062): toda imagem publicada precisa ter um client
# NSwag publicado com o MESMO contrato. Para cada produto com contrato, compara o openapi.json do
# HEAD com o da última tag estável <produto>-client/vX.Y.Z alcançável. Se divergir, a release
# para: o client tem que sair antes da imagem.
#
#   scripts/check-image-contracts.sh <versao-da-plataforma>
#
# Em sucesso imprime a linha da tabela de compatibilidade (Markdown).
set -euo pipefail

VERSION="${1:?uso: check-image-contracts.sh <versao-da-plataforma>}"
row="| $VERSION |"

for product in SecureGate LogStream NotificationHub; do
	prefix="$(printf '%s' "$product" | tr '[:upper:]' '[:lower:]')-client/v"
	tag="$(git tag --merged HEAD -l "${prefix}*" | grep -E "^${prefix}[0-9]+\.[0-9]+\.[0-9]+$" | sort -V | tail -1 || true)"
	[ -n "$tag" ] || { echo "nenhuma tag ${prefix}X.Y.Z alcançável a partir do HEAD" >&2; exit 1; }

	contract="src/$product/Secco.$product.Api/openapi/"
	if ! git diff --quiet "$tag" HEAD -- "$contract"; then
		echo "contrato de $product diverge de $tag: publique o client antes da imagem" >&2
		exit 1
	fi

	row="$row ${tag#"$prefix"} |"
done

echo "$row"
