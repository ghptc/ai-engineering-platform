#!/usr/bin/env bash
set -euo pipefail
project_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
runtime_dir="$project_root/backend/.local-runtime"
cd "$project_root"
owned_container() {
  test "$(docker inspect --format '{{index .Config.Labels "aep.local-runtime"}}' "$1")" = "$project_root" || { echo "Refusing to manage a container not owned by this runtime: $1" >&2; return 1; }
}
case "${1:-help}" in
  build)
    docker build -t aep-executor-runtime:local backend/executor/runtime
    dotnet publish backend/src/AIEngineeringPlatform.LocalExecutor -c Release -o "$runtime_dir/daemon" -m:1
    docker build -t ai-engineering-platform:local .
    ;;
  configure)
    shift
    python3 backend/scripts/configure-local-runtime.py "$@"
    ;;
  daemon)
    export EXECUTOR_CONFIG="$runtime_dir/executor.json"
    export ASPNETCORE_ENVIRONMENT=Production
    exec dotnet "$runtime_dir/daemon/AIEngineeringPlatform.LocalExecutor.dll"
    ;;
  up)
    test -S "$runtime_dir/socket/executor.sock" || { echo 'Start the executor daemon in another terminal first.' >&2; exit 1; }
    docker network inspect aep-local >/dev/null 2>&1 || docker network create aep-local
    if ! docker container inspect aep-local-mongo >/dev/null 2>&1; then
      docker run -d --name aep-local-mongo --label "aep.local-runtime=$project_root" --memory 512m --cpus 1 --pids-limit 256 --network aep-local --restart unless-stopped -v aep-local-mongo:/data/db mongo:8.0
    else
      owned_container aep-local-mongo
      docker start aep-local-mongo
    fi
    app_mounts=(--mount "type=bind,src=$runtime_dir/socket,dst=/run/aep-executor,readonly")
    if test -f "$runtime_dir/github-key-path"; then
      key_path="$(cat "$runtime_dir/github-key-path")"
      app_mounts+=(--mount "type=bind,src=$key_path,dst=/run/secrets/github-app.pem,readonly")
    fi
    docker run -d --name aep-local-app --label "aep.local-runtime=$project_root" --network aep-local --restart unless-stopped \
      --env-file "$runtime_dir/application.env" --group-add "$(id -g)" \
      --publish 127.0.0.1:8080:8080 "${app_mounts[@]}" ai-engineering-platform:local
    echo 'Open http://localhost:8080. MongoDB is separate; the daemon remains outside the application container.'
    ;;
  down)
    owned_container aep-local-app
    owned_container aep-local-mongo
    docker stop aep-local-app aep-local-mongo
    echo 'Containers stopped; database and executor state preserved. Stop the foreground daemon separately.'
    ;;
  test)
    export EXECUTOR_TEST_IMAGE
    EXECUTOR_TEST_IMAGE="$(docker image inspect aep-executor-runtime:local --format '{{.Id}}')"
    RUN_LOCAL_EXECUTOR_TESTS=1 dotnet test backend/tests/AIEngineeringPlatform.LocalExecutor.Tests -m:1
    ;;
  *)
    echo 'Usage: backend/scripts/local-runtime.sh {build|configure --repository OWNER/NAME --installation-id ID|daemon|up|down|test}'
    ;;
esac
