using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Secco.SDK.RelyingParty;

/// <summary>Composição da custódia do token da pessoa (ADR-0046).</summary>
public static class SeccoRelyingPartyExtensions
{
	private const int DefaultExpiresInSeconds = 300;

	/// <summary>
	/// Liga a custódia ao cookie + OpenID Connect já registrados pela aplicação: no <c>OnTokenValidated</c>, access e
	/// refresh token vão para o cofre e só o id da sessão entra no cookie; no <c>OnValidatePrincipal</c>, cookie
	/// cuja sessão não está no cofre é rejeitado (volta ao login). Os eventos que a aplicação já definiu são
	/// preservados e encadeados. Exige <c>SaveTokens = false</c> no OpenID Connect (o token não pode ir ao cookie).
	/// </summary>
	/// <param name="services">Coleção de serviços.</param>
	/// <param name="configure">Ajuste das opções (esquemas, nome da claim, validade da sessão).</param>
	public static IServiceCollection AddSeccoRelyingPartyTokens(
		this IServiceCollection services,
		Action<SeccoRelyingPartyOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);

		var optionsBuilder = services.AddOptions<SeccoRelyingPartyOptions>();

		if (configure is not null)
		{
			optionsBuilder.Configure(configure);
		}

		// Padrão em memória (ADR-0035); registre outro IDistributedCache (Redis) para várias réplicas
		services.AddDistributedMemoryCache();
		services.AddHttpContextAccessor();
		services.TryAddScoped<ISeccoUserContext, HttpContextSeccoUserContext>();
		services.TryAddSingleton<ISeccoUserSessionStore, DistributedSeccoUserSessionStore>();
		services.TryAddScoped<ISeccoTokenRefresher, SeccoTokenRefresher>();
		services.TryAddScoped<ISeccoUserTokenProvider, SeccoUserTokenProvider>();
		services.TryAddTransient<SeccoUserTokenHandler>();

		// Os esquemas vêm das opções, resolvidas na hora de configurar o esquema
		services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, CookieValidationConfigurator>();
		services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>, TokenCustodyConfigurator>();

		return services;
	}

	/// <summary>
	/// Anexa o Bearer da pessoa logada às requisições do <c>HttpClient</c> (ADR-0046) — serve a qualquer client,
	/// sem o SDK depender de nenhum. Ver as ressalvas de escopo em <see cref="SeccoUserTokenHandler"/>.
	/// </summary>
	/// <param name="builder">Builder do <c>HttpClient</c> nomeado ou tipado.</param>
	public static IHttpClientBuilder AddSeccoUserToken(this IHttpClientBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		builder.Services.TryAddTransient<SeccoUserTokenHandler>();
		return builder.AddHttpMessageHandler<SeccoUserTokenHandler>();
	}

	private sealed class CookieValidationConfigurator(IOptions<SeccoRelyingPartyOptions> options)
		: IPostConfigureOptions<CookieAuthenticationOptions>
	{
		public void PostConfigure(string? name, CookieAuthenticationOptions cookie)
		{
			if (name != options.Value.CookieScheme)
			{
				return;
			}

			var claimType = options.Value.SessionIdClaim;
			var scheme = options.Value.CookieScheme;
			var previous = cookie.Events.OnValidatePrincipal;

			cookie.Events.OnValidatePrincipal = async context =>
			{
				// Sessão sem cofre (expirou ou o processo reiniciou) não vale: volta ao login
				var store = context.HttpContext.RequestServices.GetRequiredService<ISeccoUserSessionStore>();

				if (context.Principal?.FindFirst(claimType)?.Value is not { Length: > 0 } sessionId
					|| await store.GetAsync(sessionId, context.HttpContext.RequestAborted).ConfigureAwait(false) is null)
				{
					context.RejectPrincipal();
					await context.HttpContext.SignOutAsync(scheme).ConfigureAwait(false);
					return;
				}

				await previous(context).ConfigureAwait(false);
			};
		}
	}

	private sealed class TokenCustodyConfigurator(IOptions<SeccoRelyingPartyOptions> options)
		: IPostConfigureOptions<OpenIdConnectOptions>
	{
		public void PostConfigure(string? name, OpenIdConnectOptions oidc)
		{
			if (name != options.Value.OpenIdConnectScheme)
			{
				return;
			}

			var claimType = options.Value.SessionIdClaim;
			var previous = oidc.Events.OnTokenValidated;

			oidc.Events.OnTokenValidated = async context =>
			{
				await previous(context).ConfigureAwait(false);

				// Um evento da aplicação que já falhou ou tratou a resposta encerra o fluxo
				if (context.Result is not null)
				{
					return;
				}

				if (context.TokenEndpointResponse is not { AccessToken.Length: > 0, RefreshToken.Length: > 0 } tokens
					|| context.Principal?.Identity is not ClaimsIdentity identity)
				{
					context.Fail("O emissor não devolveu access e refresh token.");
					return;
				}

				var sessionId = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
				var expiresIn = int.TryParse(tokens.ExpiresIn, out var seconds) ? seconds : DefaultExpiresInSeconds;

				await context.HttpContext.RequestServices.GetRequiredService<ISeccoUserSessionStore>().SetAsync(
					sessionId,
					new SeccoUserSession(
						tokens.AccessToken,
						tokens.RefreshToken,
						DateTimeOffset.UtcNow.AddSeconds(expiresIn),
						SeccoUserSession.ParseScopes(tokens.Scope)),
					context.HttpContext.RequestAborted).ConfigureAwait(false);

				identity.AddClaim(new Claim(claimType, sessionId));
			};
		}
	}
}
