using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Elevation;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Infrastructure.Identity;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Endpoint de token OIDC (ADR-0022): client credentials (máquinas, Fase 6.2) e
/// authorization code / refresh token (usuários, Fase 6.5) e token exchange por elevação
/// (ADR-0031). O OpenIddict valida credenciais,
/// PKCE e o próprio code/refresh ANTES do passthrough — aqui apenas montamos a identidade
/// com as claims curtas da ADR-0007.
/// </summary>
public static class TokenEndpoints
{
	/// <summary>Mapeia o endpoint de token.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapTokenEndpoints(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapPost("/connect/token", async (
			HttpContext context,
			IOpenIddictScopeManager scopeManager,
			IOpenIddictApplicationManager applicationManager,
			UserManager<User> userManager,
			SignInManager<User> signInManager) =>
		{
			var request = context.GetOpenIddictServerRequest()
				?? throw new InvalidOperationException("Requisição OIDC não encontrada no contexto.");

			if (request.IsClientCredentialsGrantType())
			{
				return await HandleClientCredentialsAsync(context, request, scopeManager, applicationManager);
			}

			if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
			{
				return await HandleUserGrantAsync(context, scopeManager, userManager, signInManager);
			}

			if (request.IsTokenExchangeGrantType())
			{
				return await HandleElevationAsync(context, request, scopeManager, userManager, signInManager);
			}

			return UnsupportedGrant("Grant type não suportado.");
		})
		.AllowAnonymous()               // a autenticação AQUI é o client_secret/PKCE, validado pelo OpenIddict
		.ExcludeFromDescription();      // endpoint de protocolo: descrito pelo discovery OIDC, não pelo contrato de negócio

		return endpoints;
	}

	private static async Task<IResult> HandleClientCredentialsAsync(
		HttpContext context,
		OpenIddictRequest request,
		IOpenIddictScopeManager scopeManager,
		IOpenIddictApplicationManager applicationManager)
	{
		// Credenciais e permissões de scope já validadas pelo OpenIddict (client_secret hasheado)
		var identity = new ClaimsIdentity(
			TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);

		// Claims curtas (ADR-0007): sub = client; sem tenant_id em serviço-a-serviço —
		// o tenant alvo viaja no header X-Tenant-Id (cenário interno, ADR-0005)
		identity.SetClaim(Claims.Subject, request.ClientId);
		identity.SetScopes(request.GetScopes());

		// Roles do client (Fase 6.4, ADR-0021): máquinas usam o MESMO modelo
		// Role + Permission dos usuários — a claim curta 'role' sai no access token
		if (await applicationManager.FindByClientIdAsync(request.ClientId!, context.RequestAborted)
			is Secco.SecureGate.Infrastructure.OpenIddict.OidcApplication { Roles.Length: > 0 } application)
		{
			identity.SetClaims(Claims.Role,
				[.. application.Roles.Split(' ', StringSplitOptions.RemoveEmptyEntries)]);
		}

		identity.SetResources(await OidcPrincipalBuilder.ResolveResourcesAsync(
			scopeManager, identity.GetScopes(), context.RequestAborted));
		identity.SetDestinations(static _ => [Destinations.AccessToken]);

		return Results.SignIn(new ClaimsPrincipal(identity),
			properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
	}

	private static async Task<IResult> HandleUserGrantAsync(
		HttpContext context,
		IOpenIddictScopeManager scopeManager,
		UserManager<User> userManager,
		SignInManager<User> signInManager)
	{
		// O principal vem do code/refresh que o OpenIddict já validou
		var stored = (await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;

		var user = stored?.GetClaim(Claims.Subject) is { } subject
			? await userManager.FindByIdAsync(subject)
			: null;

		// Re-deriva as claims do banco a cada emissão (ADR-0020): usuário desativado/bloqueado
		// ou com role alterado é refletido no refresh — sem esperar o token expirar
		if (user is null || !await signInManager.CanSignInAsync(user))
		{
			return Forbid(Errors.InvalidGrant, "A conta não pode mais ser autenticada.");
		}

		var scopes = stored!.GetScopes();
		var resources = await OidcPrincipalBuilder.ResolveResourcesAsync(scopeManager, scopes, context.RequestAborted);
		var principal = OidcPrincipalBuilder.ForUser(user, await userManager.GetRolesAsync(user), scopes, resources);

		return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
	}

	/// <summary>Tipo de token aceito como <c>subject_token</c> — valor fixado pelo RFC 8693, seção 3.</summary>
	private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

	/// <summary>
	/// Token exchange por ELEVAÇÃO (ADR-0031): troca o token de um usuário por um token estreito,
	/// sem <c>tenant_id</c>, de leitura de log cross-tenant.
	/// </summary>
	/// <remarks>
	/// Antes deste método o OpenIddict já validou o client, a permissão dele para este grant e
	/// escopo, e o próprio <c>subject_token</c> (<c>ValidateSubjectToken</c>). Aqui fica a POLÍTICA.
	/// <para>
	/// Os serviços da elevação são resolvidos aqui dentro, e não injetados no endpoint: assim o
	/// caminho quente — client credentials, authorization code e refresh — não paga por eles.
	/// </para>
	/// <para>
	/// Toda recusa desta política é idêntica (invariante 6), para que a resposta não diga se o
	/// usuário existe nem por que foi recusado. As recusas anteriores, do próprio OpenIddict, têm
	/// códigos próprios — mas falam do CLIENT e do token apresentado, que o chamador já controla, e
	/// nunca do usuário.
	/// </para>
	/// </remarks>
	private static async Task<IResult> HandleElevationAsync(
		HttpContext context,
		OpenIddictRequest request,
		IOpenIddictScopeManager scopeManager,
		UserManager<User> userManager,
		SignInManager<User> signInManager)
	{
		var services = context.RequestServices;
		var auditor = services.GetRequiredService<IElevationAuditor>();

		// Sem identidade de auditoria a capacidade está desligada (invariante 7, emenda). Checado
		// ANTES de qualquer consulta: uma instalação sem auditoria não pode servir de oráculo de
		// existência de usuário.
		if (!auditor.IsConfigured)
		{
			return ElevationRefused();
		}

		// Refresh token ou qualquer outro tipo no lugar de access token: recusado aqui, e não só
		// confiado ao OpenIddict (defesa em profundidade).
		if (!string.Equals(request.SubjectTokenType, AccessTokenType, StringComparison.Ordinal))
		{
			return ElevationRefused();
		}

		// Invariante 1: exatamente o escopo elevado. Vazio também recusa — exigir o pedido explícito é
		// o que faz o OpenIddict conferir a permissão de escopo do client, que ele não confere para
		// escopo não pedido. Fora do allowlist recusa em vez de estreitar em silêncio.
		var requestedScopes = request.GetScopes();

		if (requestedScopes.Length != 1
			|| !string.Equals(requestedScopes[0], SecureGatePlatform.ElevatedScope, StringComparison.Ordinal))
		{
			return ElevationRefused();
		}

		// Principal do subject_token, já validado pelo OpenIddict
		var subject = (await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;

		if (subject is null)
		{
			return ElevationRefused();
		}

		// Invariante 4: token trocado não é re-trocável. Sem isto, o mesmo usuário renovaria o TTL do
		// próprio token elevado indefinidamente.
		if (subject.HasClaim(claim => claim.Type == SecureGatePlatform.TokenExchangeClaim))
		{
			return ElevationRefused();
		}

		// Invariante 5: subject de usuário e ativo. Client credentials tem sub = client_id, que não é
		// usuário. Tenant e nome vêm do CADASTRO, nunca de claim do token de entrada.
		var user = subject.GetClaim(Claims.Subject) is { } subjectId && Guid.TryParse(subjectId, out var userId)
			? await userManager.FindByIdAsync(userId.ToString())
			: null;

		// CanSignInAsync NÃO cobre bloqueio nem tenant desativado — só confirmação de conta. As duas
		// checagens abaixo são explícitas pelo mesmo motivo que o login federado as faz
		// (EntraSignInProcessor). Para a elevação elas importam mais que em qualquer outro fluxo: a
		// desativação de tenant é cumprida pelo CATÁLOGO, que deixa de resolver o banco daquele tenant
		// — e um token elevado lê log de OUTROS tenants, ativos, onde o catálogo resolve normalmente.
		// Sem isto, usuário de tenant desativado seguiria lendo log alheio.
		if (user is null
			|| !await signInManager.CanSignInAsync(user)
			|| await userManager.IsLockedOutAsync(user))
		{
			return ElevationRefused();
		}

		var tenant = await services.GetRequiredService<ITenantRepository>()
			.GetByIdAsync(user.TenantId, context.RequestAborted);

		if (tenant is not { IsActive: true })
		{
			return ElevationRefused();
		}

		// Autoridade (ADR-0031): concessão explícita e vigente. Revogada ou expirada, não troca.
		var now = DateTimeOffset.UtcNow;
		var grant = await services.GetRequiredService<IElevationGrantRepository>()
			.GetByUserAsync(user.Id, context.RequestAborted);

		if (grant is null || !grant.IsActiveAt(now))
		{
			return ElevationRefused();
		}

		// Invariante 3: TTL já limitado pelo teto
		var lifetime = services.GetRequiredService<ElevationOptions>().EffectiveTokenLifetime;
		string[] scopes = [SecureGatePlatform.ElevatedScope];

		// Invariante 7: auditoria ANTES da emissão. Falhou, não emite. Se a emissão falhar depois de
		// auditada, sobra um registro a mais — o lado seguro: nunca uma troca sem registro.
		var recorded = await auditor.RecordAsync(
			new ElevationAuditRecord(user.Id, user.TenantId, user.UserName, request.ClientId, scopes, now.Add(lifetime)),
			context.RequestAborted);

		if (!recorded)
		{
			return ElevationRefused();
		}

		var resources = await OidcPrincipalBuilder.ResolveResourcesAsync(scopeManager, scopes, context.RequestAborted);

		return Results.SignIn(
			OidcPrincipalBuilder.ForElevation(user, resources, lifetime),
			properties: null,
			OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
	}

	/// <summary>Recusa única da política de elevação (ADR-0031, invariante 6).</summary>
	private static IResult ElevationRefused() =>
		Forbid(Errors.InvalidGrant, "A troca de token não foi autorizada.");

	private static IResult UnsupportedGrant(string description) => Forbid(Errors.UnsupportedGrantType, description);

	private static IResult Forbid(string error, string description) =>
		Results.Forbid(
			authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
			properties: new AuthenticationProperties(new Dictionary<string, string?>
			{
				[OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
				[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
			}));
}
