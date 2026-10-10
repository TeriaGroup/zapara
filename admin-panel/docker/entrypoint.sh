#!/bin/sh
# Admin panel container entrypoint: applies the panel's own Laravel migrations, then starts Apache.
# `migrate --force` only runs migrations that have not run yet, so restarts are no-ops.
# Set ZAPARA_ADMIN_MIGRATE_ON_START=false to skip (for example when migrations are run separately).
# Before migrating, `admin:migrate-preflight` checks the schema, the CREATE privilege and conflicting tables;
# such problems stop the container at once with an "admin:" line naming the fix (required grants: README.md).
set -eu

if [ "${ZAPARA_ADMIN_MIGRATE_ON_START:-true}" = "true" ]; then
  attempt=1
  while :; do
    status=0
    php artisan admin:migrate-preflight --no-interaction || status=$?
    if [ "$status" -eq 2 ]; then
      echo "admin: migrations not started, fix the problem above or set ZAPARA_ADMIN_MIGRATE_ON_START=false and apply them separately" >&2
      exit 1
    fi
    if [ "$status" -eq 0 ]; then
      if php artisan migrate --force --no-interaction; then
        break
      fi
    fi
    if [ "$attempt" -ge 5 ]; then
      echo "admin: migrations failed after $attempt attempts, see the error above (database grants: admin-panel/README.md)" >&2
      exit 1
    fi
    attempt=$((attempt + 1))
    sleep 3
  done
fi

exec docker-php-entrypoint "$@"
