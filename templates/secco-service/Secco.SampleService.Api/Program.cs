using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using Secco.SampleService.Api.Endpoints;
using Secco.SampleService.Application;
using Secco.SampleService.Infrastructure;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.EntityFrameworkCore.Migrations;

var builder = WebApplication.CreateBuilder(args);

// Cross-cutting da plataforma: correlation + auth + tenancy + health checks + resilience (ADR-0004)
builder.Services.AddSeccoPlatform();

// OpenAPI nativo (.NET 10) com as convenções da plataforma (enums como type:string —
// ADR-0006); o snapshot versionado é validado por teste de contrato
builder.Services.AddSeccoOpenApi();

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

// Processo controlado (ADR-0005/0038): `dotnet Secco.SampleService.Api.dll migrate` aplica
// migrations de todos os tenants e o seed de referência e sai — o deploy o executa antes das réplicas.
if (SeccoCommands.IsMigrate(args))
{
	return await app.Services.RunSeccoMigrationsAsync() ? 0 : 1;
}

if (app.Environment.IsDevelopment())
{
	// UI de documentação (ADR-0006) — apenas em DEV
	app.MapScalarApiReference().AllowAnonymous();

	// Em DEV, o mesmo código do comando: F5 continua automático
	await app.Services.RunSeccoMigrationsAsync();
}

await app.RunAsync();
return 0;

/// <summary>Ponto de entrada exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
