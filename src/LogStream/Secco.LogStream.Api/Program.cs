using Scalar.AspNetCore;
using Secco.LogStream.Infrastructure;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.EntityFrameworkCore.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Cross-cutting da plataforma: correlation + tenancy + health checks + resilience (ADR-0004)
builder.Services.AddSeccoPlatform();

// OpenAPI nativo (.NET 10); o snapshot versionado é validado por teste de contrato (ADR-0006)
builder.Services.AddOpenApi();

builder.Services.AddLogStreamInfrastructure();

var app = builder.Build();

app.UseSeccoPlatform();
app.MapSeccoPlatform();

app.MapOpenApi();

if (app.Environment.IsDevelopment())
{
    // UI de documentação (ADR-0006) — apenas em DEV nesta fase
    app.MapScalarApiReference();

    // Migrations + seed automáticos SOMENTE em Development (ADR-0005: fora daqui,
    // processo controlado). O seed de desenvolvimento ainda exige a flag (ADR-0019).
    await app.Services.MigrateLogStreamTenantDatabasesAsync();
    await app.Services.SeedSeccoDataAsync();
}

await app.RunAsync();

/// <summary>Ponto de entrada exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
