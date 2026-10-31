FROM docker:28-cli AS dockercli
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
COPY backend/ ./backend/
RUN dotnet publish backend/src/AIEngineeringPlatform.LocalExecutor/AIEngineeringPlatform.LocalExecutor.csproj -c Release -m:1 -o /publish /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends git util-linux curl coreutils && rm -rf /var/lib/apt/lists/*
COPY --from=dockercli /usr/local/bin/docker /usr/bin/docker
WORKDIR /app
COPY --from=build /publish/ ./
COPY --chmod=0555 backend/executor/managed/start.sh /usr/local/bin/aep-executor-start
ENTRYPOINT ["/usr/local/bin/aep-executor-start"]
