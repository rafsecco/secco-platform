using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using Secco.LogStream.Api.Endpoints;
using Secco.LogStream.Application;
using Secco.LogStream.Infrastructure;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.EntityFrameworkCore.Migrations;
using Secco.SecureGate.Client.Authorization;
using Secco.SecureGate.Client.Catalog;

var builder = WebApplication.CreateBuilder(args);

// Cross-cutting da plataforma: correlation + auth + tenancy + health checks + resilience (ADR-0004)
builder.Services.AddSeccoPlatform();

// OpenAPI nativo (.NET 10) com as convenções da plataforma (enums como type:string —
// ADR-0006); o snapshot versionado é validado por teste de contrato
builder.Services.AddSeccoOpenApi();

// Enums viajam como string no contrato (ex.: "Error" em vez de 4)
builder.Services.ConfigureHttpJsonOptions(options =>
	options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Options (LogStream:Ingestion, LogStream:Retention, LogStream:Database) são bindadas
// lazy pela Infrastructure a partir do IConfiguration do host
builder.Services.AddLogStreamApplication();
builder.Services.AddLogStreamInfrastructure();

// Catálogo de tenants central (Fase 6.3, ADR-0005): com a seção Secco:SecureGate configurada,
// o catálogo servido pelo SecureGate assume; sem ela (DEV), segue o catálogo por configuração
builder.Services.AddSecureGateTenantCatalog();

// Resolução de permissões central (Fase 6.4, ADR-0021): mesma decisão lazy — sem a seção,
// o resolver por configuração do SDK (Secco:Authorization:Roles) segue valendo em DEV
builder.Services.AddSecureGatePermissionResolver();

var app = builder.Build();

app.UseSeccoPlatform();
app.MapSeccoPlatform();
app.MapLogEntryEndpoints();
app.MapLogProcessEndpoints();
app.MapApiCallLogEndpoints();
app.MapAuditEntryEndpoints();

// Contrato é público por design (ADR-0006) — exceção explícita à FallbackPolicy
app.MapOpenApi().AllowAnonymous();

// Processo controlado (ADR-0005/0038): `dotnet Secco.LogStream.Api.dll migrate` aplica migrations
// de todos os tenants e o seed de referência e sai — o deploy o executa antes das réplicas.
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
