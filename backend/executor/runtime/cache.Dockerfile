# Owner-prepared dependency data only. Never execute repository scripts here.
ARG RUNTIME_IMAGE=aep-executor-runtime:local
FROM ${RUNTIME_IMAGE}
USER root
COPY --chown=65532:65532 nuget/ /opt/nuget-packages/
COPY --chown=65532:65532 npm/ /opt/npm-cache/
USER 65532:65532
