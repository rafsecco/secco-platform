using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using Secco.SampleService.Api.Endpoints;
using Secco.SampleService.Application;
using Secco.SampleService.Infrastructure;
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

// Options (SampleService:*) são bindadas lazy pela Infrastructure a partir do IConfiguration
builder.Services.AddSampleServiceApplication();
builder.Services.AddSampleServiceInfrastructure();

var app = builder.Build();

app.UseSeccoPlatform();
app.MapSeccoPlatform();
app.MapSampleEndpoints();

// Contrato é público por design (ADR-0006) — exceção explícita à FallbackPolicy
app.MapOpenApi().AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    // UI de documentação (ADR-0006) — apenas em DEV
    app.MapScalarApiReference().AllowAnonymous();

    // Migrations + seed automáticos SOMENTE em Development (ADR-0005: fora daqui,
    // processo controlado). O seed de desenvolvimento ainda exige a flag (ADR-0019).
    await app.Services.MigrateSampleServiceTenantDatabasesAsync();
    await app.Services.SeedSeccoDataAsync();
}

await app.RunAsync();

/// <summary>Ponto de entrada exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
