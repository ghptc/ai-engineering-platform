FROM node:24-bookworm-slim AS node
FROM node:24-bookworm-slim AS npm-cache
WORKDIR /approved
COPY backend/executor/managed/dependencies/frontend/package*.json ./
RUN npm ci --ignore-scripts --cache /opt/npm-cache --no-audit --no-fund && rm -rf /opt/npm-cache/_logs /opt/npm-cache/_update-notifier-last-checked
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS nuget-cache
WORKDIR /approved
COPY backend/executor/managed/dependencies/ApprovedTests.csproj ./
RUN dotnet restore ApprovedTests.csproj --packages /opt/nuget-packages -p:NuGetAudit=false
FROM mcr.microsoft.com/dotnet/sdk:10.0
COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm && apt-get update && apt-get install -y --no-install-recommends python3 && rm -rf /var/lib/apt/lists/*
COPY --from=npm-cache /opt/npm-cache/_cacache/ /opt/npm-cache/
COPY --from=nuget-cache /opt/nuget-packages/ /opt/nuget-packages/
COPY --chmod=0555 backend/executor/runtime/runner.py /usr/local/bin/aep-run
COPY --chmod=0444 backend/executor/managed/profiles.py /opt/aep/profiles.py
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 NUGET_PACKAGES=/opt/nuget-packages npm_config_cache=/tmp/npm
USER 65532:65532
WORKDIR /workspace
ENTRYPOINT ["/usr/local/bin/aep-run"]
