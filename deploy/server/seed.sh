#!/bin/sh
set -eu
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://127.0.0.1:8080
dotnet /app/Zapara.Server.dll >/tmp/seed-server.log 2>&1 &
pid=$!
ok=0
i=0
while [ "$i" -lt 60 ]; do
    if curl -sf http://127.0.0.1:8080/health/live >/dev/null; then
        ok=1
        break
    fi
    i=$((i + 1))
    sleep 1
done
if [ "$ok" -ne 1 ]; then
    echo "seed server did not become live" >&2
    tail -n 40 /tmp/seed-server.log >&2 || true
    kill "$pid" 2>/dev/null || true
    exit 1
fi
code=$(curl -sS -o /tmp/user.json -w '%{http_code}' \
    -H 'content-type: application/json' \
    -d "{\"username\":\"platform.admin\",\"password\":\"${ADMIN_PASSWORD}\",\"displayName\":\"Администратор\"}" \
    http://127.0.0.1:8080/api/v1/auth/register || true)
if [ "$code" != "201" ]; then
    echo "register status ${code}" >&2
    kill "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
    exit 1
fi
user=$(sed -n 's/.*"userId":"\([^"]*\)".*/\1/p' /tmp/user.json)
rm -f /tmp/user.json
dotnet /opt/admincli/Zapara.AdminCli.dll admin bootstrap --user-id "$user"
kill "$pid" 2>/dev/null || true
wait "$pid" 2>/dev/null || true
echo "bootstrap committed ${user}"
