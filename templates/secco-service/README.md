# Secco.SampleService

Produto da Secco Platform gerado pelo template `secco-service` — nasce conforme todas as ADRs: 4 camadas (ADR-0002), `AddSeccoPlatform()` (ADR-0004), multi-tenancy database-per-tenant (ADR-0005), contrato OpenAPI versionado com client NSwag (ADR-0006), nomenclatura de banco por convention (ADR-0017), dois providers (ADR-0018) e testes (ADR-0012).

## Pós-geração (checklist)

1. **Mover os testes** para a raiz de testes do monorepo e ajustar o caminho do `ProjectReference` no csproj:
   `git mv src/SampleService/tests tests/SampleService`
2. **Gerar as migrations iniciais** (uma por engine, ADR-0018):
   ```bash
   dotnet ef migrations add Initial --project src/SampleService/Secco.SampleService.Migrations.SqlServer --output-dir Migrations
   dotnet ef migrations add Initial --project src/SampleService/Secco.SampleService.Migrations.Postgres --output-dir Migrations
   ```
3. **Adicionar os projetos à solution**: `dotnet sln Secco.Platform.slnx add src/SampleService/**/*.csproj tests/SampleService/**/*.csproj`
4. **Gerar o snapshot do contrato**: rodar os testes com `SECCO_UPDATE_OPENAPI=true` e commitar o `openapi/openapi.json` — o projeto Client compila a partir dele.
5. **CI**: adicionar o path filter do produto no `.github/workflows/ci.yml` (padrão do LogStream).
6. **Apagar o recurso Sample** (pastas `Samples/` + `SampleEndpoints`) quando o domínio real começar — ele existe como referência executável dos padrões.

## O recurso Sample demonstra

- Entidade `BaseEntity` (Guid v7) com guarda de invariante e colunas por convention (`tb_samples`, `ds_name`...).
- Handler com `Result<T>` (ADR-0004) e limites de entrada (ADR-0020) via options com bind lazy.
- Endpoints protegidos pela `FallbackPolicy`, `ToHttpResult()` → ProblemDetails, paginação `PagedResult<T>`.
- Testes unitários (fake da porta) e de integração com SQL Server real (Testcontainers), incluindo isolamento físico entre tenants e teste de contrato OpenAPI.
