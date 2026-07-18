using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using Secco.NotificationHub.Api.Endpoints;
using Secco.NotificationHub.Application;
using Secco.NotificationHub.Infrastructure;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.EntityFrameworkCore.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Cross-cutting da plataforma: correlation + auth + tenancy + health checks + resilience (ADR-0004)
builder.Services.AddSeccoPlatform();

// OpenAPI nativo (.NET 10) com as convenções da plataforma (enums como type:string —
// ADR-0006); o snapshot versionado é validado por teste de contrato
builder.Services.AddSeccoOpenApi();

// Enums viajam como string no contrato
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Options (NotificationHub:*) são bindadas lazy pela Infrastructure a partir do IConfiguration
builder.Services.AddNotificationHubApplication();
builder.Services.AddNotificationHubInfrastructure();

// Jobs persistentes com retry (ADR-0015 Camada 2) — banco de PLATAFORMA, não por tenant;
// não faz parte de AddSeccoPlatform() (produtos sem essa necessidade não carregam Hangfire)
builder.Services.AddNotificationHubBackgroundJobs();

var app = builder.Build();

app.UseSeccoPlatform();
app.MapSeccoPlatform();
app.MapNotificationEndpoints();

// Contrato é público por design (ADR-0006) — exceção explícita à FallbackPolicy
app.MapOpenApi().AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    // UI de documentação (ADR-0006) — apenas em DEV
    app.MapScalarApiReference().AllowAnonymous();

    // Migrations + seed automáticos SOMENTE em Development (ADR-0005: fora daqui,
    // processo controlado). O seed de desenvolvimento ainda exige a flag (ADR-0019).
    await app.Services.MigrateNotificationHubTenantDatabasesAsync();
    await app.Services.SeedSeccoDataAsync();
}

await app.RunAsync();

/// <summary>Ponto de entrada exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
