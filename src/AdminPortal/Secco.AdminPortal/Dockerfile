# syntax=docker/dockerfile:1
# Contexto de build: a RAIZ do monorepo (Central Package Management exige os props):
#   docker build -f src/AdminPortal/Secco.AdminPortal/Dockerfile -t secco-adminportal .

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG SECCO_VERSION=0.0.0
ARG SECCO_INCLUDE_DEV_SETTINGS=false
WORKDIR /src

COPY Directory.Build.props Directory.Build.targets Directory.Packages.props nuget.config .editorconfig ./
COPY src/SharedKernel/ src/SharedKernel/
COPY src/SDK/ src/SDK/
# Os clients NSwag são gerados a partir do contrato publicado das APIs (ADR-0006)
COPY src/SecureGate/Secco.SecureGate.Client/ src/SecureGate/Secco.SecureGate.Client/
COPY src/SecureGate/Secco.SecureGate.Api/openapi/ src/SecureGate/Secco.SecureGate.Api/openapi/
COPY src/LogStream/Secco.LogStream.Client/ src/LogStream/Secco.LogStream.Client/
COPY src/LogStream/Secco.LogStream.Api/openapi/ src/LogStream/Secco.LogStream.Api/openapi/
COPY src/AdminPortal/ src/AdminPortal/

# MinVerSkip: dentro da imagem não há repositório git; a versão entra por SECCO_VERSION (CI, ADR-0011).
# appsettings.Development.json só entra com SECCO_INCLUDE_DEV_SETTINGS=true (compose de dev)
RUN dotnet publish src/AdminPortal/Secco.AdminPortal/Secco.AdminPortal.csproj \
    --configuration Release --output /app /p:UseAppHost=false /p:MinVerSkip=true /p:Version=${SECCO_VERSION} \
 && if [ "$SECCO_INCLUDE_DEV_SETTINGS" != "true" ]; then rm -f /app/appsettings.Development.json; fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Chaves do DataProtection persistidas num volume (1654 = $APP_UID da imagem aspnet)
RUN mkdir -p /var/lib/secco-adminportal/keys \
 && chown 1654:1654 /var/lib/secco-adminportal/keys \
 && chmod 0700 /var/lib/secco-adminportal/keys
ENV AdminPortal__DataProtection__KeysPath=/var/lib/secco-adminportal/keys
VOLUME /var/lib/secco-adminportal/keys
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["dotnet", "Secco.AdminPortal.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "Secco.AdminPortal.dll"]
