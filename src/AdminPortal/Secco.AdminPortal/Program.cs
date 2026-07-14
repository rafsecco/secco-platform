using Secco.AdminPortal.Authentication;
using Secco.AdminPortal.Components;
using Secco.AdminPortal.Endpoints;
using Secco.AdminPortal.Services;
using Secco.SDK.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server (ADR-0023): render interativo por circuito
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

// Cross-cutting não-de-auth do SDK — o AdminPortal é relying party, não resource server
builder.Services.AddSeccoCorrelation();
builder.Services.AddSeccoResilience();
builder.Services.AddSeccoHealthChecks();

// Relying party OIDC: cookie + authorization code/PKCE contra o SecureGate
builder.Services.AddAdminPortalAuthentication(builder.Configuration, builder.Environment);

// Client do SecureGate (herda a resiliência do SDK); o token do operador é anexado por chamada
builder.Services.AddHttpClient(AdminPortalDefaults.SecureGateHttpClient, (serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["Secco:SecureGate:ApiBaseUrl"]
        ?? configuration["Secco:SecureGate:Authority"]
        ?? throw new InvalidOperationException("Configure 'Secco:SecureGate:Authority' (ou ApiBaseUrl).");

    client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
});

builder.Services.AddScoped<ITenantAdminService, SecureGateTenantAdminService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseSeccoCorrelation();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Health é anônimo (sem FallbackPolicy neste app); os componentes exigem autenticação
app.MapSeccoHealthChecks();
app.MapAuthenticationEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization();

await app.RunAsync();

/// <summary>Ponto de entrada exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
