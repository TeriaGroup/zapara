#!/bin/sh
# Admin panel container entrypoint: applies the panel's own Laravel migrations, then starts Apache.
# `migrate --force` only runs migrations that have not run yet, so restarts are no-ops.
# Set ZAPARA_ADMIN_MIGRATE_ON_START=false to skip (for example when migrations are run separately).
set -eu

if [ "${ZAPARA_ADMIN_MIGRATE_ON_START:-true}" = "true" ]; then
  attempt=1
  until php artisan migrate --force --no-interaction; do
    if [ "$attempt" -ge 5 ]; then
      echo "admin: migrations failed after $attempt attempts" >&2
      exit 1
    fi
    attempt=$((attempt + 1))
    sleep 3
  done
fi

exec docker-php-entrypoint "$@"
