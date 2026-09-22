#!/bin/sh
# Copy the Caddy certificate for mail.voen.teriahost.ru into the maddy TLS directory.
set -eu
host=mail.voen.teriahost.ru
src="/caddy/caddy/certificates/acme-v02.api.letsencrypt.org-directory/${host}"
dst=/certs
mkdir -p "$dst"
while true; do
  if [ -s "${src}/${host}.crt" ] && [ -s "${src}/${host}.key" ]; then
    if ! cmp -s "${src}/${host}.crt" "$dst/fullchain.pem" || ! cmp -s "${src}/${host}.key" "$dst/privkey.pem"; then
      cp "${src}/${host}.crt" "$dst/fullchain.pem"
      cp "${src}/${host}.key" "$dst/privkey.pem"
      chmod 644 "$dst/fullchain.pem" "$dst/privkey.pem"
    fi
  fi
  sleep 300
done
