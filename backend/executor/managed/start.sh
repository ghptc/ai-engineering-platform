#!/bin/sh
set -eu
umask 077
mkdir -p /run/aep-executor /var/lib/aep-executor/control-home
chown 0:1654 /run/aep-executor
chmod 2750 /run/aep-executor
if [ ! -f /run/aep-executor/control-token ]; then
    head -c 48 /dev/urandom | base64 > /run/aep-executor/control-token
fi
chown 0:1654 /run/aep-executor/control-token
chmod 0640 /run/aep-executor/control-token
exec dotnet /app/AIEngineeringPlatform.LocalExecutor.dll
