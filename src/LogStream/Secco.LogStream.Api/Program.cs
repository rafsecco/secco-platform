using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using Secco.LogStream.Api.Endpoints;
using Secco.LogStream.Application;
using Secco.LogStream.Infrastructure;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.EntityFrameworkCore.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Cross-cutting da plataforma: correlation + auth + tenancy + health checks + resilience (ADR-0004)
builder.Services.AddSeccoPlatform();

// OpenAPI nativo (.NET 10); o snapshot versionado é validado por teste de contrato (ADR-0006)
builder.Services.AddOpenApi();

// Enums viajam como string no contrato (ex.: "Error" em vez de 4)
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddLogStreamApplication(options =>
    builder.Configuration.GetSection("LogStream:Ingestion").Bind(options));
builder.Services.AddLogStreamInfrastructure();

var app = builder.Build();

app.UseSeccoPlatform();
app.MapSeccoPlatform();
app.MapLogEntryEndpoints();
app.MapLogProcessEndpoints();
app.MapApiCallLogEndpoints();

// Contrato é público por design (ADR-0006) — exceção explícita à FallbackPolicy
app.MapOpenApi().AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    // UI de documentação (ADR-0006) — apenas em DEV nesta fase
    app.MapScalarApiReference().AllowAnonymous();

    // Migrations + seed automáticos SOMENTE em Development (ADR-0005: fora daqui,
    // processo controlado). O seed de desenvolvimento ainda exige a flag (ADR-0019).
    await app.Services.MigrateLogStreamTenantDatabasesAsync();
    await app.Services.SeedSeccoDataAsync();
}

await app.RunAsync();

/// <summary>Ponto de entrada exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
