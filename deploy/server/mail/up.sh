#!/bin/sh
# Start mail on the compose host. Does not print passwords and does not touch Postgres or .env.
set -eu
cd "$(dirname "$0")/.."
domain=voen.teriahost.ru
host=mail.${domain}
secrets=./mail-secrets
mkdir -p "$secrets"
chmod 700 "$secrets"

passfile() {
  printf '%s/%s.password' "$secrets" "$1"
}

ensure_pass() {
  file=$(passfile "$1")
  if [ ! -s "$file" ]; then
    openssl rand -base64 24 | tr -d '\n' > "$file"
    chmod 600 "$file"
  fi
}

docker compose up -d caddy
docker compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile
cert="/data/caddy/certificates/acme-v02.api.letsencrypt.org-directory/${host}/${host}.crt"
i=0
while [ "$i" -lt 40 ]; do
  if docker compose exec -T caddy test -f "$cert"; then
    break
  fi
  i=$((i + 1))
  sleep 3
done
docker compose exec -T caddy test -f "$cert"

docker compose up -d certsync
i=0
while [ "$i" -lt 20 ]; do
  if docker compose exec -T certsync test -s /certs/privkey.pem; then
    break
  fi
  i=$((i + 1))
  sleep 2
done
docker compose exec -T certsync test -s /certs/privkey.pem

docker compose up -d maddy
i=0
while [ "$i" -lt 30 ]; do
  if docker compose exec -T maddy maddy creds list >/dev/null 2>&1; then
    break
  fi
  i=$((i + 1))
  sleep 2
done
docker compose exec -T maddy maddy creds list >/dev/null

create_box() {
  localpart=$1
  addr="${localpart}@${domain}"
  ensure_pass "$localpart"
  if ! docker compose exec -T maddy maddy creds list | grep -F "$addr" >/dev/null; then
    docker compose exec -T maddy maddy creds create "$addr" < "$(passfile "$localpart")"
  fi
  if ! docker compose exec -T maddy maddy imap-acct list | grep -F "$addr" >/dev/null; then
    docker compose exec -T maddy maddy imap-acct create "$addr"
  fi
}

create_box feedback
create_box postmaster
docker compose up -d webmail
echo "mailboxes ready: feedback@${domain} postmaster@${domain}"
echo "passwords are in ${secrets} and were not printed"
