using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using Secco.SecureGate.Api.Endpoints;
using Secco.SecureGate.Api.Extensions;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure;
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

// Telas de login/logout (Fase 6.5) — únicas páginas server-rendered do produto
builder.Services.AddRazorPages(options => options.Conventions.AllowAnonymousToPage("/Login"));

// Options (SecureGate:*) são bindadas lazy pela Infrastructure a partir do IConfiguration
builder.Services.AddSecureGateApplication();
builder.Services.AddSecureGateInfrastructure();

// ASP.NET Identity para o login interativo (cookie não-default; o padrão segue JwtBearer)
builder.Services.AddSecureGateIdentity(builder.Environment);

// Servidor OIDC (ADR-0022): client credentials (máquinas) + authorization code/PKCE (usuários)
builder.Services.AddSecureGateOpenIddict(builder.Environment, builder.Configuration);

// Login federado Entra ID por tenant (ADR-0026) — desligado sem a seção SecureGate:EntraId
builder.Services.AddSecureGateEntraFederation(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseSeccoPlatform();
app.MapSeccoPlatform();
app.MapRazorPages();
app.MapTokenEndpoints();
app.MapInteractiveEndpoints();
app.MapFederatedLoginEndpoints();
app.MapTenantEndpoints();
app.MapCatalogEndpoints();
app.MapRoleEndpoints();
app.MapAuthorizationEndpoints();
app.MapUserEndpoints();

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
