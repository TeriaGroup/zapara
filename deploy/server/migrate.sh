#!/bin/sh
set -eu
dotnet /opt/ingest/Zapara.Ingest.dll db-init
dotnet /opt/admincli/Zapara.AdminCli.dll accounts db-migrate
dotnet /opt/admincli/Zapara.AdminCli.dll sync db-migrate
dotnet /opt/admincli/Zapara.AdminCli.dll communities db-migrate
