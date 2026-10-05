FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY server/ ./
COPY ingest/ /opt/ingest/
COPY admincli/ /opt/admincli/
COPY migrate.sh seed.sh /opt/zapara/
RUN chmod 755 /opt/zapara/migrate.sh /opt/zapara/seed.sh \
    && mkdir -p /var/lib/zapara/keys
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Zapara.Server.dll"]
