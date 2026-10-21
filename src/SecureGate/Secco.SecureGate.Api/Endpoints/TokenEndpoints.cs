using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Api.Tenancy;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Elevation;
using Secco.SecureGate.Application.InstallationOperators;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Domain.Elevation;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SharedKernel.Constants;
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
			SignInManager<User> signInManager,
			ITenantRepository tenants) =>
		{
			var request = context.GetOpenIddictServerRequest()
				?? throw new InvalidOperationException("Requisição OIDC não encontrada no contexto.");

			if (request.IsClientCredentialsGrantType())
			{
				return await HandleClientCredentialsAsync(context, request, scopeManager, applicationManager, tenants);
			}

			if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
			{
				return await HandleUserGrantAsync(context, scopeManager, userManager, signInManager);
			}

			if (request.IsTokenExchangeGrantType())
			{
				// Dois destinos de troca, pelo escopo pedido: securegate:admin é o operador designado (ADR-0049);
				// qualquer outro segue a política da elevação (ADR-0031), que recusa o que não for o escopo dela
				return request.GetScopes() is [SecureGateScopes.Admin]
					? await HandleDesignatedOperatorAsync(context, request, scopeManager, userManager, signInManager)
					: await HandleElevationAsync(context, request, scopeManager, userManager, signInManager);
			}

			return UnsupportedGrant("Grant type não suportado.");
		})
		.RequirePlatformScope(scope => scope.EnterPlatform("emissão de token: a pessoa é achada sem tenant; perfis e concessão são lidos no escopo do tenant (ADR-0040)"))
		.AllowAnonymous()               // a autenticação AQUI é o client_secret/PKCE, validado pelo OpenIddict
		.ExcludeFromDescription();      // endpoint de protocolo: descrito pelo discovery OIDC, não pelo contrato de negócio

		return endpoints;
	}

	private static async Task<IResult> HandleClientCredentialsAsync(
		HttpContext context,
		OpenIddictRequest request,
		IOpenIddictScopeManager scopeManager,
		IOpenIddictApplicationManager applicationManager,
		ITenantRepository tenants)
	{
		// Credenciais e permissões de scope já validadas pelo OpenIddict (client_secret hasheado)
		var application = await applicationManager.FindByClientIdAsync(request.ClientId!, context.RequestAborted)
			as Secco.SecureGate.Infrastructure.OpenIddict.OidcApplication;

		var identity = new ClaimsIdentity(
			TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);

		// Claims curtas (ADR-0007): sub = client
		identity.SetClaim(Claims.Subject, request.ClientId);
		identity.SetScopes(request.GetScopes());

		if (application?.TenantId is { } tenantId)
		{
			// Client de PRODUTO (ADR-0037): o token carrega o tenant do vínculo, e a regra de conflito
			// do TenantResolver passa a conter o client nele. Desativar o tenant precisa parar as
			// máquinas dele também — sem esta checagem, renovariam token a cada 5 minutos para sempre.
			if (await tenants.GetByIdAsync(tenantId, context.RequestAborted) is not { IsActive: true })
			{
				return Forbid(Errors.InvalidClient, "O client não pode obter token.");
			}

			// Defesa em profundidade: o registro já barra, mas o banco pode ser editado à mão
			if (request.GetScopes().Any(scope => !SecureGateScopes.IsProductScope(scope)))
			{
				return Forbid(Errors.InvalidScope, "Escopo não permitido a este client.");
			}

			identity.SetClaim(SeccoClaims.TenantId, tenantId.ToString());
		}

		// Sem tenant: client de PLATAFORMA (ADR-0037), só nasce da configuração da instalação — o
		// tenant alvo viaja no header X-Tenant-Id (caminho "sem claim → header", ADR-0005/0024).

		// Roles do client (Fase 6.4, ADR-0021): máquinas usam o MESMO modelo
		// Role + Permission dos usuários — a claim curta 'role' sai no access token
		if (application is { Roles.Length: > 0 })
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

		// Re-deriva do cadastro a cada emissão (ADR-0020): papel alterado reflete na renovação, e o
		// ESTADO da conta também — usuário desativado ou bloqueado, e tenant desativado, encerram a
		// sessão na próxima renovação. Com a expiração deslizante do refresh token (padrão do
		// OpenIddict), esta checagem é o ÚNICO ponto em que uma sessão em uso termina.
		if (user is null
			|| !await AccountStateGuard.CanReceiveTokensAsync(
				user,
				userManager,
				signInManager,
				context.RequestServices.GetRequiredService<ITenantRepository>(),
				context.RequestAborted))
		{
			return Forbid(Errors.InvalidGrant, "A conta não pode mais ser autenticada.");
		}

		// Os scopes também são re-derivados, e não copiados do token anterior: sem o filtro aqui, um
		// operador retirado do perfil seguiria renovando securegate:admin indefinidamente.
		// ADR-0040: o tenant é o do token emitido (nunca de parâmetro) e o vínculo é re-checado a cada
		// emissão: vínculo suspenso/removido encerra a sessão NAQUELE tenant, mesmo com vínculo ativo em outro.
		var loginTenants = context.RequestServices.GetRequiredService<LoginTenantResolver>();
		var loginTenantId = LoginTenantResolver.ForRenewal(stored!, user);

		if (!await loginTenants.CanIssueAsync(user, loginTenantId, context.RequestAborted))
		{
			return Forbid(Errors.InvalidGrant, "A conta não pode mais ser autenticada.");
		}

		var roles = await loginTenants.GetRolesAsync(user.Id, loginTenantId, context.RequestAborted);
		// ADR-0044: concessão revogada ou segundo fator desligado tiram o escopo de admin de tenant na renovação
		var scopes = await TenantAdminScopePolicy.FilterScopesAsync(
			user,
			InstallationOperatorPolicy.FilterScopes(user, roles, stored!.GetScopes(), loginTenantId),
			context.RequestServices.GetRequiredService<ITenantAdminGrantRepository>(),
			context.RequestServices.GetRequiredService<ITenantAdminAuditor>(),
			context.RequestAborted);
		var resources = await OidcPrincipalBuilder.ResolveResourcesAsync(scopeManager, scopes, context.RequestAborted);
		// auth_time e amr vêm do token anterior: renovar não é autenticar de novo (ADR-0049)
		var principal = OidcPrincipalBuilder.ForUser(
			user, roles, scopes, resources, loginTenantId,
			OidcPrincipalBuilder.AuthenticatedAt(stored), OidcPrincipalBuilder.MethodsOf(stored));

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

		// Estado atual da conta: bloqueio, desativação e tenant desativado. Para a elevação isto importa
		// mais que em qualquer outro fluxo — a desativação de tenant é cumprida pelo CATÁLOGO, e um
		// token elevado lê log de OUTROS tenants, ativos, onde o catálogo resolve normalmente.
		if (user is null
			|| !await AccountStateGuard.CanReceiveTokensAsync(
				user,
				userManager,
				signInManager,
				services.GetRequiredService<ITenantRepository>(),
				context.RequestAborted))
		{
			return ElevationRefused();
		}

		// Autoridade (ADR-0031): concessão explícita e vigente. Revogada ou expirada, não troca.
		var now = DateTimeOffset.UtcNow;
		ElevationGrant? grant;

		// A concessão vive no tenant dono da conta (ADR-0031), que é conhecido: lê-se nele
		using (services.GetRequiredService<ITenantAccessScope>().EnterTenant(user.TenantId))
		{
			grant = await services.GetRequiredService<IElevationGrantRepository>()
				.GetByUserAsync(user.Id, context.RequestAborted);
		}

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

	/// <summary>
	/// Troca por OPERADOR DESIGNADO (ADR-0049): a pessoa que a instalação designou na configuração troca o próprio
	/// token por um token de operador curto, sem tenant e sem refresh — desde que tenha se autenticado há pouco e com
	/// segundo fator. Toda recusa é a mesma resposta da elevação: não diz quem é designado nem o que faltou.
	/// </summary>
	private static async Task<IResult> HandleDesignatedOperatorAsync(
		HttpContext context,
		OpenIddictRequest request,
		IOpenIddictScopeManager scopeManager,
		UserManager<User> userManager,
		SignInManager<User> signInManager)
	{
		var services = context.RequestServices;
		var auditor = services.GetRequiredService<ITenantAdminAuditor>();

		// Sem trilha, sem autoridade — checado antes de qualquer consulta, para não servir de oráculo
		if (!auditor.IsConfigured
			|| !string.Equals(request.SubjectTokenType, AccessTokenType, StringComparison.Ordinal))
		{
			return ElevationRefused();
		}

		var subject = (await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;

		// Token trocado não troca de novo: nem elevação vira operador, nem operador renova o próprio TTL
		if (subject is null || subject.HasClaim(claim => claim.Type == SecureGatePlatform.TokenExchangeClaim))
		{
			return ElevationRefused();
		}

		var user = subject.GetClaim(Claims.Subject) is { } subjectId && Guid.TryParse(subjectId, out var userId)
			? await userManager.FindByIdAsync(userId.ToString())
			: null;

		if (user is null
			|| !await AccountStateGuard.CanReceiveTokensAsync(
				user,
				userManager,
				signInManager,
				services.GetRequiredService<ITenantRepository>(),
				context.RequestAborted))
		{
			return ElevationRefused();
		}

		var options = services.GetRequiredService<IOptionsMonitor<InstallationOperatorOptions>>().CurrentValue;
		var now = services.GetRequiredKeyedService<TimeProvider>(InstallationOperatorOptions.ClockKey).GetUtcNow();
		var authenticatedAt = OidcPrincipalBuilder.AuthenticatedAt(subject);

		// Designação da instalação, segundo fator cadastrado, e autenticação desta sessão recente E com segundo
		// fator: um cookie roubado sozinho gera token comum, nunca token de operador
		// A sessão do token de entrada ainda vale (ADR-0032): sem isto, um token tirado antes de "encerrar sessões"
		// ainda viraria token de operador durante os minutos de vida dele
		if (!services.GetRequiredService<IInstallationOperatorDesignations>().IsDesignated(user.Id)
			|| subject.GetClaim(SeccoClaims.SessionVersion) != SessionVersion.From(user.SecurityStamp)
			|| !user.TwoFactorEnabled
			|| !OidcPrincipalBuilder.MethodsOf(subject).Contains("mfa", StringComparer.Ordinal)
			|| authenticatedAt is null
			|| now - authenticatedAt.Value > options.EffectiveRecentAuthentication)
		{
			return ElevationRefused();
		}

		var lifetime = options.EffectiveTokenLifetime;

		// Auditoria ANTES da emissão, no tenant dono da pessoa: falhou, não emite
		var recorded = await auditor.RecordAsync(
			new TenantAdminAuditRecord(
				user.Id,
				string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName,
				user.TenantId,
				request.ClientId,
				"DesignatedOperatorTokenIssued",
				TargetUserId: null,
				AdministeredTenantId: null,
				Authority: SecureGateScopes.Admin),
			context.RequestAborted);

		if (!recorded)
		{
			return ElevationRefused();
		}

		var resources = await OidcPrincipalBuilder.ResolveResourcesAsync(
			scopeManager, [SecureGateScopes.Admin], context.RequestAborted);

		return Results.SignIn(
			OidcPrincipalBuilder.ForDesignatedOperator(user, resources, lifetime),
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
