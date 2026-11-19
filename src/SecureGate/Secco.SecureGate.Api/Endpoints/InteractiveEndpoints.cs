using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Api.Tenancy;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Infrastructure.Federation;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SharedKernel.Constants;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Endpoints interativos do OIDC (Fase 6.5, ADR-0022): <c>/connect/authorize</c> (login via
/// cookie do Identity → emissão do authorization code), <c>/connect/userinfo</c> e
/// <c>/connect/logout</c>. Endpoints de protocolo — descritos pelo discovery OIDC, não pelo
/// contrato de negócio (<c>ExcludeFromDescription</c>).
/// </summary>
public static class InteractiveEndpoints
{
	/// <summary>Mapeia os endpoints interativos do OIDC.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapInteractiveEndpoints(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapMethods("/connect/authorize", ["GET", "POST"], AuthorizeAsync)
			.RequirePlatformScope(scope => scope.EnterPlatform("authorize: a pessoa autenticada pelo cookie é achada sem tenant; perfis lidos no escopo do tenant do login (ADR-0040)"))
			.AllowAnonymous()
			.ExcludeFromDescription();

		// Cast a Delegate: UserInfo tem só (HttpContext) e casaria com RequestDelegate (ASP0016)
		endpoints.MapMethods("/connect/userinfo", ["GET", "POST"], (Delegate)UserInfo)
			.AllowAnonymous()
			.ExcludeFromDescription();

		endpoints.MapMethods("/connect/logout", ["GET", "POST"], LogoutAsync)
			.AllowAnonymous()
			.ExcludeFromDescription();

		return endpoints;
	}

	private static async Task<IResult> AuthorizeAsync(
		HttpContext context,
		UserManager<User> userManager,
		SignInManager<User> signInManager,
		IOpenIddictScopeManager scopeManager)
	{
		var request = context.GetOpenIddictServerRequest()
			?? throw new InvalidOperationException("Requisição OIDC não encontrada no contexto.");

		// Sem cookie de sessão → /login, ou direto ao Entra quando o tenant do client ligou a opção (ADR-0026)
		var authentication = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);

		if (!authentication.Succeeded)
		{
			return await ChallengeWithoutSessionAsync(context, request);
		}

		// ADR-0032: o cookie só vale se o stamp ainda é o do login e a conta pode receber token. Sem isto, um
		// cookie roubado geraria tokens novos — já com a versão nova — depois de qualquer revogação.
		var user = await signInManager.ValidateSecurityStampAsync(authentication.Principal);

		if (user is null
			|| !await AccountStateGuard.CanReceiveTokensAsync(
				user,
				userManager,
				signInManager,
				context.RequestServices.GetRequiredService<ITenantRepository>(),
				context.RequestAborted))
		{
			await context.SignOutAsync(IdentityConstants.ApplicationScheme);

			return Results.Challenge(
				new AuthenticationProperties
				{
					RedirectUri = context.Request.PathBase + context.Request.Path + context.Request.QueryString,
				},
				[IdentityConstants.ApplicationScheme]);
		}

		// Reautenticação pedida pela aplicação (ADR-0049): prompt=login, ou max_age mais curto que a idade da
		// autenticação desta sessão. A sessão do SecureGate sai e a pessoa autentica de novo — senha e, se ela
		// tiver, segundo fator. A volta tira o "login" do prompt; senão o pedido nunca terminaria.
		var authenticatedAt = FreshAuthentication.AuthenticatedAt(authentication.Principal);
		var tooOld = request.MaxAge is { } maxAge
			&& (authenticatedAt is null || DateTimeOffset.UtcNow - authenticatedAt.Value > TimeSpan.FromSeconds(maxAge));

		if (request.HasPromptValue(PromptValues.Login) || tooOld)
		{
			if (request.HasPromptValue(PromptValues.None))
			{
				return Results.Forbid(
					authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
					properties: new AuthenticationProperties(new Dictionary<string, string?>
					{
						[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
						[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "A aplicação pediu nova autenticação.",
					}));
			}

			await context.SignOutAsync(IdentityConstants.ApplicationScheme);

			// Com login direto a reautenticação também vai ao Entra, forçando o prompt; a volta já sem o "login"
			// no prompt e com a marca de tentativa (guarda contra laço)
			var reauthTarget = await context.RequestServices.GetRequiredService<DirectSignInPolicy>()
				.ResolveAsync(request.ClientId, context.RequestAborted);

			if (reauthTarget is not null)
			{
				return Results.Challenge(
					EntraDirectChallenge.Create(
						SignInMarkers.WithDirectAttempt(WithoutLoginPrompt(context, request)),
						reauthTarget,
						LoginHint.Sanitize(request.LoginHint),
						forceLogin: true),
					[SecureGateEntraIdOptions.AuthenticationScheme]);
			}

			return Results.Challenge(
				new AuthenticationProperties { RedirectUri = WithoutLoginPrompt(context, request) },
				[IdentityConstants.ApplicationScheme]);
		}

		// ADR-0040: o tenant do login vem do registro do client, nunca de parâmetro. Sem vínculo ativo
		// (ou com o tenant do login inativo) a pessoa não entra NESTA aplicação, mas a sessão dela segue
		// valendo nas outras — por isso access_denied ao redirect_uri e nada de SignOut.
		var loginTenants = context.RequestServices.GetRequiredService<LoginTenantResolver>();
		var loginTenantId = await loginTenants.ForAuthorizeAsync(request.ClientId, user, context.RequestAborted);

		if (!await loginTenants.CanIssueAsync(user, loginTenantId, context.RequestAborted))
		{
			return Results.Forbid(
				authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
				properties: new AuthenticationProperties(new Dictionary<string, string?>
				{
					[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.AccessDenied,
					[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
						"A conta não tem acesso a esta aplicação.",
				}));
		}

		var roles = await loginTenants.GetRolesAsync(user.Id, loginTenantId, context.RequestAborted);

		// Operador de instalação sem segundo fator não recebe código de autorização (entrega D):
		// é a conta que lê log de todos os tenants por elevação (ADR-0031). O desvio leva ao
		// cadastro e volta a esta mesma requisição depois — nenhum relying party precisa saber.
		if (OperatorTwoFactorPolicy.RequiresEnrollment(user, roles, user.TwoFactorEnabled, loginTenantId))
		{
			var destino = context.Request.PathBase + context.Request.Path + context.Request.QueryString;

			return Results.Redirect(
				OperatorTwoFactorPolicy.EnrollmentPath + "?returnUrl=" + Uri.EscapeDataString(destino));
		}

		// ADR-0044: admin de tenant também não recebe code sem segundo fator — mesma razão do operador,
		// em escala menor: a conta administra outros tenants
		var tenantAdminGrants = context.RequestServices.GetRequiredService<ITenantAdminGrantRepository>();
		var tenantAdminAuditor = context.RequestServices.GetRequiredService<ITenantAdminAuditor>();

		if (await TenantAdminScopePolicy.RequiresEnrollmentAsync(
				user, request.GetScopes(), tenantAdminGrants, tenantAdminAuditor, context.RequestAborted))
		{
			var destino = context.Request.PathBase + context.Request.Path + context.Request.QueryString;

			return Results.Redirect(
				OperatorTwoFactorPolicy.EnrollmentPath + "?returnUrl=" + Uri.EscapeDataString(destino));
		}

		// ADR-0023: o scope admin só é emitido a operadores de instalação — login de usuário
		// comum pelo client do AdminPortal NÃO escala para admin, mesmo com o scope permitido.
		// O nome do role é único só por tenant: exigir também o tenant de plataforma impede
		// que um operador forjado (role com o mesmo nome) num tenant de cliente ganhe o scope
		// admin (defesa contra colisão de nome, ADR-0020/0023/0024).
		var granted = await TenantAdminScopePolicy.FilterScopesAsync(
			user,
			InstallationOperatorPolicy.FilterScopes(user, roles, request.GetScopes(), loginTenantId),
			tenantAdminGrants,
			tenantAdminAuditor,
			context.RequestAborted);
		var resources = await OidcPrincipalBuilder.ResolveResourcesAsync(scopeManager, granted, context.RequestAborted);

		// Client first-party confiável (ConsentType Implicit no registro) → sem tela de consent (Fase 6.5)
		var principal = OidcPrincipalBuilder.ForUser(
			user, roles, granted, resources, loginTenantId,
			authenticatedAt, FreshAuthentication.MethodsOf(authentication.Principal));

		return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
	}

	// Sem sessão: a decisão vem só do registro do client e do banco (ADR-0020) — parâmetros da requisição só
	// conseguem DESLIGAR o modo direto (prompt=none, marcas de laço), nunca ligá-lo
	private static async Task<IResult> ChallengeWithoutSessionAsync(HttpContext context, OpenIddictRequest request)
	{
		var currentUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;

		var target = await context.RequestServices.GetRequiredService<DirectSignInPolicy>()
			.ResolveAsync(request.ClientId, context.RequestAborted);

		if (target is null)
		{
			// Após o login, a pessoa volta exatamente a esta requisição de autorização
			return Results.Challenge(
				new AuthenticationProperties { RedirectUri = currentUrl },
				[IdentityConstants.ApplicationScheme]);
		}

		if (request.HasPromptValue(PromptValues.None))
		{
			return Results.Forbid(
				authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
				properties: new AuthenticationProperties(new Dictionary<string, string?>
				{
					[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
					[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "É preciso autenticar a pessoa.",
				}));
		}

		// Já foi ao Entra nesta volta e continua sem sessão: tela de login com o erro, nunca um laço
		if (SignInMarkers.HasDirectAttempt(context.Request))
		{
			return Results.Redirect(FederatedFailureRedirect.For(currentUrl));
		}

		// Acabou de sair: manda ao /saiu, que não autentica ninguém sozinho
		if (SignInMarkers.HasSignedOut(context, DateTimeOffset.UtcNow))
		{
			return Results.Redirect("/saiu?returnUrl=" + Uri.EscapeDataString(currentUrl));
		}

		return Results.Challenge(
			EntraDirectChallenge.Create(
				SignInMarkers.WithDirectAttempt(currentUrl),
				target,
				LoginHint.Sanitize(request.LoginHint),
				forceLogin: false),
			[SecureGateEntraIdOptions.AuthenticationScheme]);
	}

	// A mesma requisição de autorização, sem o "login" no prompt e sem max_age: a autenticação nova já atende
	private static string WithoutLoginPrompt(HttpContext context, OpenIddictRequest request)
	{
		var query = QueryHelpers.ParseQuery(context.Request.QueryString.Value)
			.Where(pair => pair.Key is not (Parameters.Prompt or Parameters.MaxAge))
			.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString(), StringComparer.Ordinal);

		var prompt = string.Join(' ', request.GetPromptValues().Where(value => value != PromptValues.Login));

		if (prompt.Length > 0)
		{
			query[Parameters.Prompt] = prompt;
		}

		return QueryHelpers.AddQueryString(context.Request.PathBase + context.Request.Path, query);
	}

	private static async Task<IResult> UserInfo(HttpContext context)
	{
		var principal = (await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;

		if (principal is null)
		{
			return Results.Unauthorized();
		}

		var claims = new Dictionary<string, object?>
		{
			[Claims.Subject] = principal.GetClaim(Claims.Subject),
			[SeccoClaims.TenantId] = principal.GetClaim(SeccoClaims.TenantId),
		};

		if (principal.HasScope(Scopes.Email))
		{
			claims[Claims.Email] = principal.GetClaim(Claims.Email);
		}

		if (principal.HasScope(Scopes.Profile))
		{
			claims[Claims.Name] = principal.GetClaim(Claims.Name);
		}

		if (principal.HasScope(Scopes.Roles))
		{
			claims[Claims.Role] = principal.GetClaims(SeccoClaims.Role);
		}

		return Results.Json(claims);
	}

	private static async Task<IResult> LogoutAsync(HttpContext context, SignInManager<User> signInManager)
	{
		// Encerra a sessão local (cookie do Identity)...
		await signInManager.SignOutAsync();

		// ...e delega ao OpenIddict a validação do post_logout_redirect_uri e o redirecionamento
		return Results.SignOut(
			new AuthenticationProperties { RedirectUri = "/" },
			[OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
	}
}
