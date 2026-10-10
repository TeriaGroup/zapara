FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
# Application files stay root-owned and read-only for the runtime user.
COPY server/ ./
COPY ingest/ /opt/ingest/
COPY admincli/ /opt/admincli/
COPY migrate.sh seed.sh /opt/zapara/
# The base image ships the unprivileged "app" user ($APP_UID, 1654). Only the data directories are writable;
# fresh named volumes mounted there inherit this ownership.
RUN chmod 755 /opt/zapara/migrate.sh /opt/zapara/seed.sh \
    && mkdir -p /var/lib/zapara/keys /var/lib/zapara/media \
    && chown -R "$APP_UID:$APP_UID" /var/lib/zapara \
    && chmod 700 /var/lib/zapara/keys
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Zapara.Server.dll"]
