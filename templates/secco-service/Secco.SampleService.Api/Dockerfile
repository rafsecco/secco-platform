# syntax=docker/dockerfile:1
# Contexto de build: a RAIZ do monorepo (Central Package Management exige os props):
#   docker build -f src/SampleService/Secco.SampleService.Api/Dockerfile -t secco-sampleservice .

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG SECCO_VERSION=0.0.0
ARG SECCO_INCLUDE_DEV_SETTINGS=false
WORKDIR /src

COPY Directory.Build.props Directory.Build.targets Directory.Packages.props nuget.config .editorconfig ./
COPY src/SharedKernel/ src/SharedKernel/
COPY src/SDK/ src/SDK/
# A Infrastructure publica o catálogo de permissões no SecureGate (ADR-0039); o client NSwag é
# gerado a partir do contrato publicado da API (ADR-0006), então o COPY leva o client e o openapi
COPY src/SecureGate/Secco.SecureGate.Client/ src/SecureGate/Secco.SecureGate.Client/
COPY src/SecureGate/Secco.SecureGate.Api/openapi/ src/SecureGate/Secco.SecureGate.Api/openapi/
COPY src/SampleService/ src/SampleService/

# MinVerSkip: dentro da imagem não há repositório git; a versão entra por SECCO_VERSION (CI, ADR-0011).
# appsettings.Development.json só entra com SECCO_INCLUDE_DEV_SETTINGS=true (compose de dev)
RUN dotnet publish src/SampleService/Secco.SampleService.Api/Secco.SampleService.Api.csproj \
    --configuration Release --output /app /p:UseAppHost=false /p:MinVerSkip=true /p:Version=${SECCO_VERSION} \
 && if [ "$SECCO_INCLUDE_DEV_SETTINGS" != "true" ]; then rm -f /app/appsettings.Development.json; fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["dotnet", "Secco.SampleService.Api.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "Secco.SampleService.Api.dll"]
