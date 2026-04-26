# syntax=docker/dockerfile:1.7
#
# Crawlers ASP.NET Core / SignalR server.
# Multi-stage: build with the SDK image, ship only the published output on the
# slimmer aspnet runtime image. Runs as the non-root 'app' user that ships in
# the .NET base images.

ARG DOTNET_VERSION=9.0

# --- build ---
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Copy csproj files first so `dotnet restore` can be cached across rebuilds
# unless dependencies actually change.
COPY src/Crawlers.Domain/Crawlers.Domain.csproj src/Crawlers.Domain/
COPY src/Crawlers.Generation/Crawlers.Generation.csproj src/Crawlers.Generation/
COPY src/Crawlers.Server/Crawlers.Server.csproj src/Crawlers.Server/

RUN dotnet restore src/Crawlers.Server/Crawlers.Server.csproj

# Bring in the rest of the source and publish.
COPY src/ src/

RUN dotnet publish src/Crawlers.Server/Crawlers.Server.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# --- runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# curl is used by the HEALTHCHECK below. Install before dropping privileges.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Drop to the non-root user that ships with the aspnet base image.
USER $APP_UID

# Listen on 8080 inside the container; the host port is set in compose.
# The aspnet base image already sets ASPNETCORE_HTTP_PORTS=8080, so don't
# also set ASPNETCORE_URLS here — doing both produces an "Overriding
# HTTP_PORTS" warning at startup.
ENV DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Crawlers.Server.dll"]
