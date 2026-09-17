using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.AspNetCore.Authentication;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>
/// Validação de sessão para aplicações que autenticam por cookie a partir do login OIDC (ADR-0032): a
/// revogação na plataforma alcança quem já está logado na aplicação.
/// </summary>
public static class SeccoCookieSessionValidationExtensions
{
	/// <summary>
	/// Confere a <c>sver</c> do cookie a cada requisição HTTP. Exige um <see cref="ISessionVersionResolver"/>
	/// registrado — sem ele o startup falha, para a validação nunca ficar desligada sem ninguém perceber.
	/// </summary>
	/// <param name="services">Coleção de serviços.</param>
	/// <param name="cookieScheme">Esquema do cookie de sessão da aplicação.</param>
	public static IServiceCollection AddSeccoCookieSessionValidation(this IServiceCollection services, string cookieScheme)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentException.ThrowIfNullOrWhiteSpace(cookieScheme);

		services.AddSeccoSessionVersionChecking();
		services.AddSingleton<IStartupFilter, SessionResolverRequiredStartupFilter>();

		services.PostConfigure<CookieAuthenticationOptions>(cookieScheme, options =>
		{
			var previous = options.Events.OnValidatePrincipal;
			options.Events.OnValidatePrincipal = async context =>
			{
				await previous(context).ConfigureAwait(false);

				if (context.Principal is null)
				{
					return;
				}

				var checker = context.HttpContext.RequestServices.GetRequiredService<SessionVersionChecker>();

				if (!await checker.IsCurrentAsync(context.Principal, context.HttpContext.RequestAborted).ConfigureAwait(false))
				{
					context.RejectPrincipal();
					await context.HttpContext.SignOutAsync(cookieScheme).ConfigureAwait(false);
				}
			};
		});

		return services;
	}

	private sealed class SessionResolverRequiredStartupFilter(IServiceProvider serviceProvider) : IStartupFilter
	{
		public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
		{
			if (serviceProvider.GetService<ISessionVersionResolver>() is null)
			{
				throw new InvalidOperationException(
					"AddSeccoCookieSessionValidation exige um ISessionVersionResolver registrado " +
					"(ex.: AddSecureGateSessionVersionResolver do Secco.SecureGate.Client).");
			}

			return next;
		}
	}
}
