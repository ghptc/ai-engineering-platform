FROM node:24-bookworm-slim AS node
FROM mcr.microsoft.com/dotnet/sdk:10.0
COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm && ln -s /usr/local/lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx && apt-get update && apt-get install -y --no-install-recommends python3 && rm -rf /var/lib/apt/lists/*
COPY --chmod=0555 runner.py /usr/local/bin/aep-run
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 NUGET_PACKAGES=/opt/nuget-packages npm_config_cache=/tmp/npm
USER 65532:65532
WORKDIR /workspace
ENTRYPOINT ["/usr/local/bin/aep-run"]
