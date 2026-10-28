FROM node:24-bookworm-slim AS frontend
ENV NODE_OPTIONS=--max-old-space-size=2048
WORKDIR /build/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /build
COPY backend/ ./backend/
RUN dotnet restore backend/src/AIEngineeringPlatform.Api/AIEngineeringPlatform.Api.csproj
RUN dotnet publish backend/src/AIEngineeringPlatform.Api/AIEngineeringPlatform.Api.csproj -c Release -m:1 -o /publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=backend --chown=app:app /publish ./
COPY --from=frontend --chown=app:app /build/frontend/dist ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080 ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER app
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD curl --fail --silent http://127.0.0.1:${ASPNETCORE_HTTP_PORTS}/health/live || exit 1
ENTRYPOINT ["dotnet", "AIEngineeringPlatform.Api.dll"]
