using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Secco.SDK.RelyingParty;

/// <summary>
/// Exige que a pessoa tenha se autenticado há no máximo <see cref="MaxAge"/> (e, por padrão, com segundo fator),
/// conforme o <c>auth_time</c> e o <c>amr</c> do access token custodiado (ADR-0050) — não as claims do cookie.
/// Falhou por autenticação antiga ou desconhecida: o pacote desafia o OIDC com <c>prompt=login</c> e
/// <c>max_age</c>, voltando à mesma URL. Falhou só por falta de <c>mfa</c> numa autenticação recente: <c>403</c>
/// (desafiar de novo levaria uma conta sem segundo fator a um laço de login).
/// </summary>
/// <param name="maxAge">Idade máxima da autenticação.</param>
/// <param name="requireMfa">Se exige <c>mfa</c> no <c>amr</c>.</param>
public sealed class SeccoRecentAuthenticationRequirement(TimeSpan maxAge, bool requireMfa = true) : IAuthorizationRequirement
{
	/// <summary>Idade máxima da autenticação.</summary>
	public TimeSpan MaxAge { get; } = maxAge > TimeSpan.Zero
		? maxAge
		: throw new ArgumentOutOfRangeException(nameof(maxAge), "A idade máxima deve ser positiva.");

	/// <summary>Se exige <c>mfa</c> no <c>amr</c>.</summary>
	public bool RequireMfa { get; } = requireMfa;
}

/// <summary>Política de autenticação recente (ADR-0050).</summary>
public static class SeccoRecentAuthenticationPolicyExtensions
{
	/// <summary>
	/// Exige autenticação recente; vale para MVC, Razor Pages e APIs mínimas (o desafio é feito pelo middleware de
	/// autorização, decorado por <c>AddSeccoRelyingPartyTokens</c>).
	/// </summary>
	/// <param name="builder">Builder da política.</param>
	/// <param name="maxAge">Idade máxima da autenticação.</param>
	/// <param name="requireMfa">Se exige <c>mfa</c> no <c>amr</c>.</param>
	public static AuthorizationPolicyBuilder RequireSeccoRecentAuthentication(
		this AuthorizationPolicyBuilder builder,
		TimeSpan maxAge,
		bool requireMfa = true)
	{
		ArgumentNullException.ThrowIfNull(builder);

		return builder.AddRequirements(new SeccoRecentAuthenticationRequirement(maxAge, requireMfa));
	}
}

/// <summary>
/// Avalia o requisito pela sessão do cofre. Não autenticado: não opina (comportamento padrão de desafio). Falha
/// com razão tipada para o decorador do resultado distinguir "refaça o login" de "403".
/// </summary>
internal sealed class SeccoRecentAuthenticationHandler(ISeccoUserAuthentication authentication)
	: AuthorizationHandler<SeccoRecentAuthenticationRequirement>
{
	internal const string StaleReason = "secco-authentication-stale";

	internal const string MfaReason = "secco-authentication-mfa-missing";

	// Tolerância a relógio entre emissor e aplicação para um auth_time ligeiramente no futuro
	private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

	protected override async Task HandleRequirementAsync(
		AuthorizationHandlerContext context,
		SeccoRecentAuthenticationRequirement requirement)
	{
		if (context.User.Identity?.IsAuthenticated != true)
		{
			return;
		}

		var current = await authentication.GetAuthenticationAsync().ConfigureAwait(false);
		var age = DateTimeOffset.UtcNow - current?.AuthenticatedAt;

		// Sem sessão, sem auth_time, vencida ou no futuro além da tolerância: autenticação não comprovada
		if (current is null || age is null || age > requirement.MaxAge || age < -ClockSkew)
		{
			context.Fail(new AuthorizationFailureReason(this, StaleReason));
			return;
		}

		if (requirement.RequireMfa && !current.HasMethod("mfa"))
		{
			context.Fail(new AuthorizationFailureReason(this, MfaReason));
			return;
		}

		context.Succeed(requirement);
	}
}

/// <summary>
/// Decora o <see cref="IAuthorizationMiddlewareResultHandler"/> da aplicação: quando a falha é de autenticação
/// antiga, desafia o OIDC com <c>prompt=login</c> e <c>max_age</c> voltando à mesma URL; o resto segue para o
/// handler decorado (o da aplicação, ou o padrão).
/// </summary>
internal sealed class SeccoRecentAuthenticationResultHandler(
	IAuthorizationMiddlewareResultHandler inner,
	IOptions<SeccoRelyingPartyOptions> options) : IAuthorizationMiddlewareResultHandler
{
	public Task HandleAsync(
		RequestDelegate next,
		HttpContext context,
		AuthorizationPolicy policy,
		PolicyAuthorizationResult authorizeResult)
	{
		if (authorizeResult.Forbidden
			&& authorizeResult.AuthorizationFailure is { } failure
			&& failure.FailureReasons.Any(reason => reason.Handler is SeccoRecentAuthenticationHandler
				&& reason.Message == SeccoRecentAuthenticationHandler.StaleReason))
		{
			// Com vários requisitos, vale o mais exigente (menor idade máxima). Da política: ao falhar com razão
			// explícita, o framework não preenche FailedRequirements
			var maxAge = policy.Requirements.OfType<SeccoRecentAuthenticationRequirement>().Min(r => r.MaxAge);

			// Só o caminho local da própria requisição. Um caminho que comece com "//" ou "/\\" seria lido pelo navegador
			// como outro host depois do callback do OIDC (redirecionamento aberto): nesse caso, volta à raiz
			// A forma ESCAPADA é a que vai ao navegador, então é ela que se confere: o navegador descarta tabulação e
			// quebra de linha, e "/\t/host" crua viraria "//host"
			var candidate = context.Request.PathBase.Add(context.Request.Path).ToUriComponent()
				+ context.Request.QueryString.ToUriComponent();
			var returnUrl = IsLocal(candidate) ? candidate : "/";

			return context.ChallengeAsync(options.Value.OpenIdConnectScheme, new OpenIdConnectChallengeProperties
			{
				Prompt = "login",
				MaxAge = maxAge,
				RedirectUri = returnUrl,
			});
		}

		return inner.HandleAsync(next, context, policy, authorizeResult);
	}

	// Mesmas regras do IsLocalUrl do ASP.NET Core: começa com uma barra só, sem "\" logo depois, e sem caractere de
	// controle ou espaço em lugar nenhum (o navegador os descarta e a URL vira outra)
	internal static bool IsLocal(string path) =>
		path.StartsWith('/')
		&& !path.StartsWith("//", StringComparison.Ordinal)
		&& !path.StartsWith("/\\", StringComparison.Ordinal)
		&& !path.Any(c => char.IsControl(c) || char.IsWhiteSpace(c));
}
