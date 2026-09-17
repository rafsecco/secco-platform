using Secco.SecureGate.Client.Catalog;
using Secco.SecureGate.Client.Authorization;
using Secco.SDK.AspNetCore.Extensions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication;
using System.Security.Cryptography;
using System.Security.Claims;
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

				// ADR-0020: este cookie custodia o access token do operador (ver OnTokenValidated) —
				// em Production não pode trafegar sem TLS. Mesmo critério de RequireHttpsMetadata
				// logo abaixo: só Production força HTTPS (Development e Testing seguem SameAsRequest,
				// hosts locais/TestServer normalmente são HTTP puro).
				options.Cookie.SecurePolicy = environment.IsProduction()
					? CookieSecurePolicy.Always
					: CookieSecurePolicy.SameAsRequest;

				options.SlidingExpiration = true;

				// Sessão sem cofre (expirou ou o AdminPortal reiniciou) não vale: volta ao login
				options.Events.OnValidatePrincipal = async context =>
				{
					var store = context.HttpContext.RequestServices.GetRequiredService<IOperatorSessionStore>();

					if (context.Principal?.FindFirst(AdminPortalDefaults.SessionIdClaim)?.Value is not { Length: > 0 } sessionId
						|| await store.GetAsync(sessionId, context.HttpContext.RequestAborted) is null)
					{
						context.RejectPrincipal();
						await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
					}
				};
			})
			.AddOpenIdConnect(options =>
			{
				options.Authority = configuration["Secco:SecureGate:Authority"];
				options.ClientId = configuration["Secco:SecureGate:ClientId"];
				options.ClientSecret = configuration["Secco:SecureGate:ClientSecret"];

				options.ResponseType = OpenIdConnectResponseType.Code;
				options.UsePkce = true;

				// O access token é custodiado como claim no cookie (ver OnTokenValidated) —
				// acessível no circuito do Blazor Server via AuthenticationStateProvider
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

				options.Events = new OpenIdConnectEvents
				{
					OnTokenValidated = async context =>
					{
						if (context.TokenEndpointResponse is not { AccessToken.Length: > 0, RefreshToken.Length: > 0 } tokens
							|| context.Principal?.Identity is not ClaimsIdentity identity)
						{
							context.Fail("O SecureGate não devolveu access e refresh token.");
							return;
						}

						var sessionId = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
						var expiresIn = int.TryParse(tokens.ExpiresIn, out var seconds) ? seconds : 300;

						await context.HttpContext.RequestServices.GetRequiredService<IOperatorSessionStore>().SetAsync(
							sessionId,
							new OperatorSession(tokens.AccessToken, tokens.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn)),
							context.HttpContext.RequestAborted);

						identity.AddClaim(new Claim(AdminPortalDefaults.SessionIdClaim, sessionId));
					},
				};
			});

		services.AddAuthorization(options =>
			options.AddPolicy(AdminPortalDefaults.OperatorPolicy, policy =>
				policy.RequireRole(AdminPortalDefaults.OperatorRole)));

		services.AddDistributedMemoryCache();
		services.AddSingleton<IOperatorSessionStore, DistributedOperatorSessionStore>();
		services.AddScoped<IOperatorTokenRefresher, OperatorTokenRefresher>();

		// ADR-0032: revogar na plataforma derruba o cookie do operador. Credenciais PRÓPRIAS: o client
		// secco-adminportal pode pedir securegate:admin e não pode ganhar client credentials.
		services.AddSecureGateSessionVersionResolver(_ =>
			configuration.GetSection(AdminPortalDefaults.SessionValidationSection).Get<SecureGateClientCredentialsOptions>()
			?? new SecureGateClientCredentialsOptions());
		services.AddSeccoCookieSessionValidation(CookieAuthenticationDefaults.AuthenticationScheme);

		services.AddScoped<IOperatorTokenProvider, OperatorTokenProvider>();

		return services;
	}
}
