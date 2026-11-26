#!/usr/bin/env bash
# Smoke do compose de implantação (ADR-0062): sobe deploy/docker-compose.yml — o exemplo do guia —
# com o override deploy/ci/compose.ci.yml, em Production, e confere o que o guia promete.
#
#   scripts/deploy-smoke.sh <registro/prefixo> <versao>
#   scripts/deploy-smoke.sh local 0.1.0        # imagens local/secco-*:0.1.0 já construídas
#
# Sai 0 só se todas as asserções passarem. Sempre derruba o stack e apaga o que criou.
set -euo pipefail

# Git Bash (Windows) reescreve argumento que começa com "/" (ex.: -subj "/CN=..."); no Linux é inócuo.
export MSYS_NO_PATHCONV=1

# Caminho que o docker (inclusive o Docker Desktop no Windows) entende
host_path() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi; }

REGISTRY="${1:?uso: deploy-smoke.sh <registro> <versao>}"
VERSION="${2:?uso: deploy-smoke.sh <registro> <versao>}"

ROOT="$(host_path "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)")"
DEPLOY="$ROOT/deploy"
WORK="$(host_path "$(mktemp -d)")"
PROJECT="secco-smoke"

SECUREGATE_HOST="login.exemplo.com.br"
LOGSTREAM_HOST="logs.exemplo.com.br"
NOTIFICATIONHUB_HOST="notificacoes.exemplo.com.br"
ADMINPORTAL_HOST="admin.exemplo.com.br"
HTTPS_PORT="${SMOKE_HTTPS_PORT:-18443}"
HTTP_PORT="${SMOKE_HTTP_PORT:-18080}"
PLATFORM_TENANT="018f0000-0000-7000-8000-0000000000ff"


fail() { echo "FALHOU: $*" >&2; exit 1; }
ok() { echo "ok - $*"; }

secret() { openssl rand -hex 24; }

compose() {
	docker compose -p "$PROJECT" --env-file "$(host_path "$WORK/.env")" \
		-f "$DEPLOY/docker-compose.yml" -f "$DEPLOY/ci/compose.ci.yml" "$@"
}

cleanup() {
	local code=$?
	if [ "$code" -ne 0 ]; then
		echo "--- diagnóstico ---" >&2
		compose ps -a >&2 || true
		compose logs --tail 80 >&2 || true
	fi
	compose down -v --remove-orphans >/dev/null 2>&1 || true
	# A pasta de chaves pertence ao uid 1654: apaga pelo próprio docker
	docker run --rm -v "$(host_path "$DEPLOY/data"):/d" busybox sh -c 'rm -rf /d/*' >/dev/null 2>&1 || true
	rm -rf "$DEPLOY/data" "$WORK" 2>/dev/null || true
	exit "$code"
}
trap cleanup EXIT

curl_https() {
	# De dentro da rede do compose, como os próprios serviços: o nome público resolve para o proxy
	# pelo alias de rede, com a CA de teste. Não depende do curl nem das portas do host.
	# O primeiro argumento (o host) fica só para leitura de quem chama. Saída em arquivo: /out.
	shift
	docker run --rm --network "${PROJECT}_secco" \
		-v "$(host_path "$WORK/certs"):/certs:ro" -v "$(host_path "$WORK/out"):/out" \
		curlimages/curl:8.11.1 -sS --max-time 15 --cacert /certs/ca.crt "$@"
}

status_of() { local host="$1"; shift; curl_https "$host" -o /dev/null -w '%{http_code}' "$@" 2>/dev/null || true; }

wait_healthy() {
	local deadline=$((SECONDS + ${2:-300})) svc="$1" id state
	while [ "$SECONDS" -lt "$deadline" ]; do
		id="$(compose ps -q "$svc" 2>/dev/null || true)"
		if [ -n "$id" ]; then
			state="$(docker inspect -f '{{.State.Health.Status}}' "$id" 2>/dev/null || echo none)"
			[ "$state" = "healthy" ] && return 0
			[ "$state" = "unhealthy" ] && fail "$svc ficou unhealthy"
		fi
		sleep 3
	done
	fail "$svc não ficou healthy a tempo"
}

token() {
	# Resposta guardada em $WORK/out/token-<client>.json: quem chama a mostra se não vier token
	curl_https "$SECUREGATE_HOST" -o "/out/token-$1.json" -X POST "https://$SECUREGATE_HOST/connect/token" \
		-d grant_type=client_credentials -d "client_id=$1" -d "client_secret=$2" -d "scope=$3" || true
	sed -n 's/.*"access_token": *"\([^"]*\)".*/\1/p' "$WORK/out/token-$1.json" 2>/dev/null || true
}

token_or_fail() {
	local value
	value="$(token "$@")"
	[ -n "$value" ] || { cat "$WORK/out/token-$1.json" >&2 2>/dev/null; fail "o SecureGate não emitiu token para $1 (escopo $3)"; }
	printf '%s' "$value"
}

jwks_kids() {
	curl_https "$SECUREGATE_HOST" "https://$SECUREGATE_HOST/.well-known/jwks" \
		| grep -o '"kid": *"[^"]*"' | tr -d ' ' | sort | tr '\n' ' '
}

keys_fingerprint() {
	docker run --rm -v "$(host_path "$DEPLOY/data/adminportal-keys"):/k:ro" busybox \
		sh -c 'cd /k && ls key-*.xml >/dev/null 2>&1 && sha256sum key-*.xml'
}

# --- Material de teste: CA, certificado TLS dos quatro domínios e PKCS#12 de assinatura ------------
mkdir -p "$WORK/certs" "$WORK/secrets" "$WORK/out"
chmod 777 "$WORK/out"
openssl req -x509 -newkey rsa:2048 -nodes -days 2 -subj "/CN=Secco Smoke CA" \
	-keyout "$WORK/certs/ca.key" -out "$WORK/certs/ca.crt" 2>/dev/null
openssl req -newkey rsa:2048 -nodes -subj "/CN=$SECUREGATE_HOST" \
	-keyout "$WORK/certs/server.key" -out "$WORK/certs/server.csr" 2>/dev/null
printf 'subjectAltName=DNS:%s,DNS:%s,DNS:%s,DNS:%s\n' \
	"$SECUREGATE_HOST" "$LOGSTREAM_HOST" "$NOTIFICATIONHUB_HOST" "$ADMINPORTAL_HOST" > "$WORK/certs/san.ext"
openssl x509 -req -in "$WORK/certs/server.csr" -CA "$WORK/certs/ca.crt" -CAkey "$WORK/certs/ca.key" \
	-CAcreateserial -days 2 -extfile "$WORK/certs/san.ext" -out "$WORK/certs/server.crt" 2>/dev/null
chmod 644 "$WORK/certs/"*

SIGNING_PASSWORD="$(secret)"
openssl req -x509 -newkey rsa:2048 -nodes -days 30 -subj "/CN=SecureGate signing" \
	-keyout "$WORK/secrets/signing.key" -out "$WORK/secrets/signing.crt" 2>/dev/null
openssl pkcs12 -export -inkey "$WORK/secrets/signing.key" -in "$WORK/secrets/signing.crt" \
	-out "$WORK/secrets/securegate-signing.pfx" -passout "pass:$SIGNING_PASSWORD"
chmod 644 "$WORK/secrets/securegate-signing.pfx"

SMOKE_CLIENT_SECRET="$(secret)"
SMOKE_ADMIN_SECRET="$(secret)"

cat > "$WORK/.env" <<EOF
SECCO_VERSION=$VERSION
SECCO_REGISTRY=$REGISTRY
SECUREGATE_HOST=$SECUREGATE_HOST
LOGSTREAM_HOST=$LOGSTREAM_HOST
NOTIFICATIONHUB_HOST=$NOTIFICATIONHUB_HOST
ADMINPORTAL_HOST=$ADMINPORTAL_HOST
SECCO_NETWORK_SUBNET=172.30.0.0/24
HTTPS_PORT=$HTTPS_PORT
HTTP_PORT=$HTTP_PORT
DB_SERVER=sqlserver,1433
DB_TRUST_SERVER_CERTIFICATE=true
SECUREGATE_DB_MIGRATOR_PASSWORD=Mg$(secret)
SECUREGATE_DB_APP_PASSWORD=Ap$(secret)
NOTIFICATIONHUB_JOBS_MIGRATOR_PASSWORD=Mg$(secret)
NOTIFICATIONHUB_JOBS_APP_PASSWORD=Ap$(secret)
SECUREGATE_CATALOG_KEY=$(openssl rand -base64 32)
SECUREGATE_SIGNING_CERT_FILE=$(host_path "$WORK/secrets/securegate-signing.pfx")
SECUREGATE_SIGNING_PASSWORD=$SIGNING_PASSWORD
LOGSTREAM_CLIENT_SECRET=$(secret)
NOTIFICATIONHUB_CLIENT_SECRET=$(secret)
ADMINPORTAL_CLIENT_SECRET=$(secret)
ADMINPORTAL_SESSIONS_CLIENT_SECRET=$(secret)
SMTP_HOST=mailhog
SMTP_PORT=1025
SMTP_USE_STARTTLS=false
SMTP_USERNAME=
SMTP_PASSWORD=
SMTP_FROM_ADDRESS=nao-responda@exemplo.com.br
SMTP_FROM_NAME=Smoke
SMOKE_CERTS_DIR=$(host_path "$WORK/certs")
SMOKE_SA_PASSWORD=Sa$(secret)
SMOKE_CLIENT_SECRET=$SMOKE_CLIENT_SECRET
SMOKE_ADMIN_SECRET=$SMOKE_ADMIN_SECRET
EOF

# Pasta das chaves do AdminPortal com o dono que o guia manda (uid 1654, modo 700)
mkdir -p "$DEPLOY/data/adminportal-keys"
docker run --rm -v "$(host_path "$DEPLOY/data/adminportal-keys"):/k" busybox \
	sh -c 'chown 1654:1654 /k && chmod 700 /k'

# --- 1 e 7. Cada imagem: não-root e sem appsettings.Development.json ------------------------------
for image in securegate logstream notificationhub adminportal; do
	ref="$REGISTRY/secco-$image:$VERSION"
	uid="$(docker run --rm --entrypoint id "$ref" -u)"
	[ "$uid" = "1654" ] || fail "$ref roda como uid $uid"
	if docker run --rm --entrypoint ls "$ref" /app/appsettings.Development.json >/dev/null 2>&1; then
		fail "$ref contém appsettings.Development.json"
	fi
	ok "$ref: uid 1654, sem settings de desenvolvimento"
done

# --- 2. migrate contra servidor inalcançável: sai 1, rápido, com mensagem e sem connection string --
compose up -d sqlserver mailhog >/dev/null
unreachable="Server=10.255.255.1,1433;Database=secco_securegate;User Id=x;Password=NaoVaza123;Encrypt=true;TrustServerCertificate=true;Connect Timeout=5"
set +e
started=$SECONDS
migrate_log="$(timeout 90 docker compose -p "$PROJECT" --env-file "$(host_path "$WORK/.env")" \
	-f "$DEPLOY/docker-compose.yml" -f "$DEPLOY/ci/compose.ci.yml" run --rm --no-deps \
	-e "SecureGate__Database__MigrationConnectionString=$unreachable" \
	-e "SecureGate__Database__ConnectionString=$unreachable" \
	securegate-migrate 2>&1)"
code=$?
set -e
elapsed=$((SECONDS - started))
[ "$code" -eq 1 ] || { echo "$migrate_log" | tail -40 >&2; fail "migrate inalcançável saiu $code (esperado 1) em ${elapsed}s"; }
echo "$migrate_log" | grep -q "Migrations:" || { echo "$migrate_log" | tail -40 >&2; fail "log do migrate sem a mensagem 'Migrations:'"; }
if echo "$migrate_log" | grep -q "NaoVaza123"; then fail "o log do migrate vazou a senha da connection string"; fi
ok "migrate contra banco inalcançável: sai 1 em ${elapsed}s, mensagem clara, sem segredo"

# --- 3 e 4. Sobe do zero: migrates saem 0, serviços ficam healthy e respondem pelo proxy ----------
compose up -d
for svc in securegate logstream notificationhub adminportal; do wait_healthy "$svc"; done
for svc in securegate-migrate logstream-migrate notificationhub-migrate; do
	exit_code="$(docker inspect -f '{{.State.ExitCode}}' "$(compose ps -a -q "$svc")")"
	[ "$exit_code" = "0" ] || fail "$svc saiu $exit_code"
done
ok "migrate dos três serviços com banco saiu 0 contra o banco vazio"

for host in "$SECUREGATE_HOST" "$LOGSTREAM_HOST" "$NOTIFICATIONHUB_HOST" "$ADMINPORTAL_HOST"; do
	code="$(status_of "$host" "https://$host/health/ready")"
	[ "$code" = "200" ] || fail "$host/health/ready respondeu $code pelo proxy"
done
ok "os quatro serviços healthy e com /health/ready 200 pelo proxy"

# --- 5. Token emitido segue aceito depois de recriar SecureGate e LogStream -----------------------
access_token="$(token_or_fail smoke-client "$SMOKE_CLIENT_SECRET" logstream)"
[ -n "$access_token" ] || fail "o SecureGate não emitiu token client_credentials"
logs_url="https://$LOGSTREAM_HOST/api/v1/log-entries"
[ "$(status_of "$LOGSTREAM_HOST" "$logs_url")" = "401" ] || fail "LogStream sem token não respondeu 401"
before="$(status_of "$LOGSTREAM_HOST" -H "Authorization: Bearer $access_token" "$logs_url")"
[ "$before" != "401" ] && [ "$before" != "000" ] || fail "LogStream recusou o token (HTTP $before)"
kids_before="$(jwks_kids)"
[ -n "$kids_before" ] || fail "JWKS sem chave"

compose up -d --force-recreate --no-deps securegate logstream >/dev/null
wait_healthy securegate
wait_healthy logstream
after="$(status_of "$LOGSTREAM_HOST" -H "Authorization: Bearer $access_token" "$logs_url")"
[ "$after" != "401" ] && [ "$after" != "000" ] || fail "o token deixou de valer depois de recriar os containers (HTTP $after)"
[ "$(jwks_kids)" = "$kids_before" ] || fail "o JWKS mudou depois de recriar o SecureGate"
ok "token emitido antes segue aceito depois de recriar SecureGate e LogStream (HTTP $after), JWKS igual"

# --- 6. Chave do DataProtection do AdminPortal sobrevive à recriação -----------------------------
keys_before="$(keys_fingerprint)" || fail "o AdminPortal não gravou chave na pasta montada"
compose up -d --force-recreate --no-deps adminportal >/dev/null
wait_healthy adminportal
[ "$(keys_fingerprint)" = "$keys_before" ] || fail "as chaves do AdminPortal mudaram depois de recriar o container"
ok "chaves do DataProtection do AdminPortal iguais depois de recriar o container"

# --- Primeiro operador pelo caminho do guia: client temporário com securegate:admin ----------------
admin_token="$(token_or_fail smoke-admin "$SMOKE_ADMIN_SECRET" securegate:admin)"
[ -n "$admin_token" ] || fail "o SecureGate não emitiu token com securegate:admin"
users_url="https://$SECUREGATE_HOST/api/v1/tenants/$PLATFORM_TENANT/users"
create="$(curl_https "$SECUREGATE_HOST" -o /out/user.json -w '%{http_code}' -X POST "$users_url" \
	-H "Authorization: Bearer $admin_token" -H "Content-Type: application/json" \
	-d '{"email":"operador@exemplo.com.br","displayName":"Primeiro operador"}')"
[ "$create" = "201" ] || { cat "$WORK/out/user.json" >&2; fail "criar a conta no tenant de plataforma respondeu $create"; }
user_id="$(sed -n 's/.*"id": *"\([^"]*\)".*/\1/p' "$WORK/out/user.json" | head -1)"
[ -n "$user_id" ] || fail "resposta da criação sem id"
assign="$(curl_https "$SECUREGATE_HOST" -o /out/role.json -w '%{http_code}' -X POST \
	"$users_url/$user_id/roles/installation-operator" -H "Authorization: Bearer $admin_token")"
[ "$assign" = "204" ] || { cat "$WORK/out/role.json" >&2; fail "atribuir installation-operator respondeu $assign"; }
ok "primeiro operador: conta criada no tenant de plataforma e installation-operator atribuído"

echo "SMOKE OK"
