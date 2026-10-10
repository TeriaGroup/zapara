#!/bin/sh
# One-time step after switching to non-root containers: volumes created by the old root containers
# keep root-owned files. This runs short-lived root containers that only chown each volume to its new owner.
# Safe to repeat. Stop the stack first: docker compose down (volumes are kept).
set -eu
cd "$(dirname "$0")"

root_run() {
  docker compose run --rm --no-deps --user 0:0 --cap-add CHOWN --cap-add DAC_OVERRIDE --cap-add FOWNER "$@"
}

own() {
  service=$1
  owner=$2
  shift 2
  root_run --entrypoint chown "$service" -R "$owner" "$@"
}

docker compose build server caddy maddy webmail
own server 1654:1654 /var/lib/zapara/keys /var/lib/zapara/media
own caddy 10001:10001 /data /config
own certsync 10001:10001 /certs
root_run --entrypoint sh maddy -c \
  'find /data -path /data/tls -prune -o -exec chown 10002:10002 {} +'
own webmail 33:33 /var/roundcube/db
echo "volume owners updated"
