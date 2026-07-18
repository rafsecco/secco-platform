# Contexto de build: a RAIZ do monorepo (Central Package Management exige os props):
#   docker build -f src/NotificationHub/Secco.NotificationHub.Api/Dockerfile -t secco-notificationhub .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Build.targets Directory.Packages.props nuget.config ./
COPY src/SharedKernel/ src/SharedKernel/
COPY src/SDK/ src/SDK/
COPY src/NotificationHub/ src/NotificationHub/

# MinVerSkip: dentro da imagem não há repositório git (versionamento é papel do CI, ADR-0011)
RUN dotnet publish src/NotificationHub/Secco.NotificationHub.Api/Secco.NotificationHub.Api.csproj \
    --configuration Release --output /app /p:UseAppHost=false /p:MinVerSkip=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Secco.NotificationHub.Api.dll"]
