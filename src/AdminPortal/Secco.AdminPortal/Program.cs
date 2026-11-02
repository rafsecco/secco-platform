using Secco.AdminPortal.Authentication;
using Secco.AdminPortal.Components;
using Secco.AdminPortal.Endpoints;
using Secco.AdminPortal.Services;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.SecurityHeaders;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server (ADR-0023): render interativo por circuito
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

// Cross-cutting não-de-auth do SDK — o AdminPortal é relying party, não resource server
builder.Services.AddSeccoCorrelation();
builder.Services.AddSeccoResilience();
builder.Services.AddSeccoHealthChecks();

// Cabeçalhos de segurança (ADR-0057): o console nunca é emoldurado. O logout posta aqui e termina
// no end_session do SecureGate, e o navegador aplica form-action aos redirects do POST — por isso
// a origem dele entra na lista. Authority HTTP (só fora de Production) fica sem form-action.
builder.Services.AddSeccoSecurityHeaders(options => options.DenyFraming());
builder.Services.AddOptions<SeccoSecurityHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
{
	// Lido na montagem da política, não no registro: fontes de configuração tardias também valem
	if (Uri.TryCreate(configuration["Secco:SecureGate:Authority"], UriKind.Absolute, out var authority)
		&& authority.Scheme == Uri.UriSchemeHttps)
	{
		options.FormActions.Add("'self'");
		options.FormActions.Add(authority.GetLeftPart(UriPartial.Authority));
	}
});

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

// Client autenticado on-behalf-of + serviços de gestão (Fase 7.1/7.2)
builder.Services.AddScoped<ISecureGateClientFactory, SecureGateClientFactory>();
builder.Services.AddScoped<ITenantAdminService, SecureGateTenantAdminService>();
builder.Services.AddScoped<IUserAdminService, SecureGateUserAdminService>();
builder.Services.AddScoped<IRoleAdminService, SecureGateRoleAdminService>();
builder.Services.AddScoped<IPermissionCatalogService, SecureGatePermissionCatalogService>();

// Leitura de logs por tenant (Fase 7.3): client do LogStream + serviço de consulta
builder.Services.AddHttpClient(AdminPortalDefaults.LogStreamHttpClient, (serviceProvider, client) =>
{
	var baseUrl = serviceProvider.GetRequiredService<IConfiguration>()["Secco:LogStream:BaseUrl"]
		?? throw new InvalidOperationException("Configure 'Secco:LogStream:BaseUrl'.");

	client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
});
builder.Services.AddScoped<ILogQueryService, LogStreamQueryService>();

// Exportação da trilha (ADR-0058): client próprio, sem o pipeline de resiliência — o retry repetiria
// uma operação que grava trilha, e o timeout por tentativa cortaria uma exportação grande.
// RemoveAllResilienceHandlers é experimental: se a API mudar numa atualização, o build quebra à vista.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient(AdminPortalDefaults.LogStreamExportHttpClient, (serviceProvider, client) =>
{
	var baseUrl = serviceProvider.GetRequiredService<IConfiguration>()["Secco:LogStream:BaseUrl"]
		?? throw new InvalidOperationException("Configure 'Secco:LogStream:BaseUrl'.");

	client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
	client.Timeout = TimeSpan.FromMinutes(5);
}).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddScoped<IAuditExportService, LogStreamAuditExportService>();
builder.Services.TryAddSingleton(TimeProvider.System);

var app = builder.Build();

app.UseSeccoSecurityHeaders();
app.UseStaticFiles();
app.UseSeccoCorrelation();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Health é anônimo (sem FallbackPolicy neste app); os componentes exigem autenticação
app.MapSeccoHealthChecks();
app.MapAuthenticationEndpoints();
app.MapAuditExportEndpoints();
app.MapRazorComponents<App>()
	.AddInteractiveServerRenderMode()
	.RequireAuthorization();

await app.RunAsync();

/// <summary>Ponto de entrada exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
