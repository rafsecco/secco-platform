using Secco.SDK.RelyingParty;
using Secco.SecureGate.Client.Catalog;
using Secco.SecureGate.Client.Authorization;
using Secco.SDK.AspNetCore.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Secco.SharedKernel.Constants;

namespace Secco.AdminPortal.Authentication;

/// <summary>
/// Composição da autenticação do AdminPortal como relying party OIDC (ADR-0023): cookie de
/// sessão + authorization code/PKCE contra o SecureGate. NÃO usa <c>AddSeccoAuthentication()</c>
/// (validação JWT de resource server) — o AdminPortal é um CLIENTE, não um resource server.
/// </summary>
public static class AdminPortalAuthenticationExtensions
{
	/// <summary>Registra cookie + OpenIdConnect e a policy de operador.</summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	/// <param name="configuration">Configuração do host (seção <c>Secco:SecureGate</c>).</param>
	/// <param name="environment">Ambiente de hospedagem.</param>
	public static IServiceCollection AddAdminPortalAuthentication(
		this IServiceCollection services,
		IConfiguration configuration,
		IHostEnvironment environment)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(environment);

		services.AddAuthentication(options =>
			{
				options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
				options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
			})
			.AddCookie(options =>
			{
				options.Cookie.Name = "secco.adminportal.auth";
				options.Cookie.HttpOnly = true;
				options.Cookie.SameSite = SameSiteMode.Lax;

				// ADR-0020: este cookie identifica a sessão que custodia o token do operador (cofre do Secco.SDK.RelyingParty, ADR-0046; o cookie leva só o id da sessão) —
				// em Production não pode trafegar sem TLS. Mesmo critério de RequireHttpsMetadata
				// logo abaixo: só Production força HTTPS (Development e Testing seguem SameAsRequest,
				// hosts locais/TestServer normalmente são HTTP puro).
				options.Cookie.SecurePolicy = environment.IsProduction()
					? CookieSecurePolicy.Always
					: CookieSecurePolicy.SameAsRequest;

				options.SlidingExpiration = true;
			})
			.AddOpenIdConnect(options =>
			{
				options.Authority = configuration["Secco:SecureGate:Authority"];
				options.ClientId = configuration["Secco:SecureGate:ClientId"];
				options.ClientSecret = configuration["Secco:SecureGate:ClientSecret"];

				options.ResponseType = OpenIdConnectResponseType.Code;
				options.UsePkce = true;

				// O token do operador vai para o cofre do Secco.SDK.RelyingParty (ADR-0046), nunca para o cookie
				options.SaveTokens = false;
				options.GetClaimsFromUserInfoEndpoint = false; // as claims já vêm no id_token

				// ADR-0007: claims curtas sem remapeamento; name = sub/username, role = 'role'
				options.MapInboundClaims = false;
				options.TokenValidationParameters.NameClaimType = "name";
				options.TokenValidationParameters.RoleClaimType = SeccoClaims.Role;

				// Fora de Production o discovery pode ser HTTP (dev local)
				options.RequireHttpsMetadata = environment.IsProduction();

				options.Scope.Clear();
				foreach (var scope in AdminPortalDefaults.Scopes)
				{
					options.Scope.Add(scope);
				}
			});

		services.AddAuthorization(options =>
			options.AddPolicy(AdminPortalDefaults.OperatorPolicy, policy =>
				policy.RequireRole(AdminPortalDefaults.OperatorRole)));

		// Custódia do token do operador (ADR-0046): cofre no servidor, cookie só com o id da sessão. O circuito do
		// Blazor não tem HttpContext, então a pessoa vem do AuthenticationStateProvider (registrar ANTES do pacote).
		// Antes de AddSeccoCookieSessionValidation: a conferência do cofre roda primeiro, a da versão da sessão depois.
		services.AddScoped<ISeccoUserContext, BlazorSeccoUserContext>();
		services.AddSeccoRelyingPartyTokens();

		// ADR-0032: revogar na plataforma derruba o cookie do operador. Credenciais PRÓPRIAS: o client
		// secco-adminportal pode pedir securegate:admin e não pode ganhar client credentials.
		services.AddSecureGateSessionVersionResolver(_ =>
			configuration.GetSection(AdminPortalDefaults.SessionValidationSection).Get<SecureGateClientCredentialsOptions>()
			?? new SecureGateClientCredentialsOptions());
		services.AddSeccoCookieSessionValidation(CookieAuthenticationDefaults.AuthenticationScheme);

		return services;
	}
}
