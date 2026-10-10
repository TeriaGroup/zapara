#!/bin/sh
# Copy the Caddy certificate for mail.voen.teriahost.ru into the maddy TLS directory.
# The private key is mode 640 with the shared group mail-tls (gid 10003), which both certsync and maddy
# join through group_add in compose.yml; it is not world-readable.
set -eu
host=mail.voen.teriahost.ru
src="/caddy/caddy/certificates/acme-v02.api.letsencrypt.org-directory/${host}"
dst=/certs
tls_gid=${MAIL_TLS_GID:-10003}
mkdir -p "$dst"
umask 027
while true; do
  if [ -s "${src}/${host}.crt" ] && [ -s "${src}/${host}.key" ]; then
    if ! cmp -s "${src}/${host}.crt" "$dst/fullchain.pem" || ! cmp -s "${src}/${host}.key" "$dst/privkey.pem"; then
      # Write to temporary files with final permissions first, then rename, so the key is never
      # briefly readable by others and maddy never sees a half-written file.
      cp "${src}/${host}.crt" "$dst/.fullchain.pem.tmp"
      cp "${src}/${host}.key" "$dst/.privkey.pem.tmp"
      chgrp "$tls_gid" "$dst/.fullchain.pem.tmp" "$dst/.privkey.pem.tmp"
      chmod 644 "$dst/.fullchain.pem.tmp"
      chmod 640 "$dst/.privkey.pem.tmp"
      mv -f "$dst/.fullchain.pem.tmp" "$dst/fullchain.pem"
      mv -f "$dst/.privkey.pem.tmp" "$dst/privkey.pem"
    fi
    # Also fixes files left world-readable by earlier versions of this script.
    chgrp "$tls_gid" "$dst/privkey.pem" "$dst/fullchain.pem"
    chmod 640 "$dst/privkey.pem"
  fi
  sleep 300
done
