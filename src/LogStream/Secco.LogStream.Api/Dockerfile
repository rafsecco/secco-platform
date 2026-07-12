# Contexto de build: a RAIZ do repositório (Central Package Management exige os props):
#   docker build -f src/LogStream/Secco.LogStream.Api/Dockerfile -t secco-logstream .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Build.targets Directory.Packages.props nuget.config ./
COPY src/SharedKernel/ src/SharedKernel/
COPY src/SDK/ src/SDK/
COPY src/LogStream/ src/LogStream/

# MinVerSkip: dentro da imagem não há repositório git (versionamento é papel do CI, ADR-0011)
RUN dotnet publish src/LogStream/Secco.LogStream.Api/Secco.LogStream.Api.csproj \
    --configuration Release --output /app /p:UseAppHost=false /p:MinVerSkip=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Secco.LogStream.Api.dll"]
