using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.EntityFrameworkCore.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Cross-cutting da plataforma: correlation + auth + tenancy + health checks + resilience (ADR-0004)
builder.Services.AddSeccoPlatform();

// OpenAPI nativo (.NET 10); o snapshot versionado é validado por teste de contrato (ADR-0006)
builder.Services.AddOpenApi();

// Enums viajam como string no contrato
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Options (SecureGate:*) são bindadas lazy pela Infrastructure a partir do IConfiguration
builder.Services.AddSecureGateApplication();
builder.Services.AddSecureGateInfrastructure();

var app = builder.Build();

app.UseSeccoPlatform();
app.MapSeccoPlatform();
// Endpoints OIDC/gestão chegam com as fases 6.2+ (client credentials, catálogo, autorização)

// Contrato é público por design (ADR-0006) — exceção explícita à FallbackPolicy
app.MapOpenApi().AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    // UI de documentação (ADR-0006) — apenas em DEV
    app.MapScalarApiReference().AllowAnonymous();

    // Migrations + seed automáticos SOMENTE em Development (ADR-0005: fora daqui,
    // processo controlado). Banco ÚNICO de plataforma (ADR-0022), não por tenant.
    await app.Services.MigrateSecureGateDatabaseAsync();
    await app.Services.SeedSeccoDataAsync();
}

await app.RunAsync();

/// <summary>Ponto de entrada exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
