using System.Globalization;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SharedKernel.Constants;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Monta o <see cref="ClaimsPrincipal"/> emitido no authorization code / refresh (Fase 6.5)
/// com as claims curtas da ADR-0007 (<c>sub</c>, <c>tenant_id</c>, <c>role</c>) e define os
/// destinos por claim: <c>role</c> e <c>tenant_id</c> vão ao access token (o LogStream os lê
/// para tenancy + autorização, ADR-0021), e as claims de perfil só ao id_token quando o
/// scope correspondente foi concedido.
/// </summary>
internal static class OidcPrincipalBuilder
{
	/// <summary>Constrói o principal de um usuário autenticado para os scopes/resources concedidos.</summary>
	/// <param name="user">Usuário autenticado.</param>
	/// <param name="roles">Roles do usuário NO tenant do login (ADR-0021, ADR-0040).</param>
	/// <param name="scopes">Scopes concedidos na requisição.</param>
	/// <param name="resources">Audiences derivadas dos scopes de produto (o que os produtos validam).</param>
	/// <param name="loginTenantId">Tenant do login: é o <c>tenant_id</c> do token (ADR-0040).</param>
	/// <param name="authenticatedAt">
	/// Instante da autenticação da sessão (<c>auth_time</c>, ADR-0049) — o do cookie no authorize, o do token
	/// anterior na renovação. Nunca o instante desta emissão.
	/// </param>
	/// <param name="methods">Métodos da autenticação (<c>amr</c>), como <c>mfa</c>.</param>
	public static ClaimsPrincipal ForUser(
		User user,
		IEnumerable<string> roles,
		IEnumerable<string> scopes,
		IEnumerable<string> resources,
		Guid loginTenantId,
		DateTimeOffset? authenticatedAt = null,
		IEnumerable<string>? methods = null)
	{
		var roleList = roles as IReadOnlyList<string> ?? [.. roles];

		var identity = new ClaimsIdentity(
			TokenValidationParameters.DefaultAuthenticationType, Claims.Name, SeccoClaims.Role);

		identity.SetClaim(Claims.Subject, user.Id.ToString());

		// ADR-0024: o operador de instalação NÃO carrega tenant_id — escolhe o tenant por
		// requisição via X-Tenant-Id (caminho "sem claim → header" da ADR-0005). Usuário
		// comum segue com tenant_id (isolamento intacto).
		// O nome do role é único apenas por tenant: exigir também que o usuário esteja no
		// tenant de plataforma impede que um operador forjado (role com o mesmo nome) num
		// tenant de cliente receba o token tenant-less (defesa contra colisão de nome,
		// ADR-0020/0023/0024).
		var isInstallationOperator = InstallationOperatorPolicy.IsInstallationOperator(user, roleList, loginTenantId);

		if (!isInstallationOperator)
		{
			identity.SetClaim(SeccoClaims.TenantId, loginTenantId.ToString());
		}

		identity.SetClaim(Claims.Email, user.Email);

		// #30: nome de exibição na claim padrão OIDC, com o e-mail como fallback quando a pessoa não
		// definiu um — mesmo valor de sempre para quem não usa o recurso, sem claim nova. Repor um
		// valor fixo aqui quebraria quem já lê "name" hoje (ex.: o rótulo de ator na auditoria do
		// secco-intranet, que cai no id quando o claim falta).
		identity.SetClaim(Claims.Name, string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName);
		identity.SetClaims(SeccoClaims.Role, [.. roleList]);
		identity.SetClaim(SeccoClaims.SessionVersion, SessionVersion.From(user.SecurityStamp));

		if (authenticatedAt is { } authTime)
		{
			identity.AddClaim(new Claim(
				Claims.AuthenticationTime,
				authTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
				ClaimValueTypes.Integer64));
		}

		identity.SetClaims(Claims.AuthenticationMethodReference, [.. (methods ?? []).Distinct(StringComparer.Ordinal)]);

		identity.SetScopes(scopes);
		identity.SetResources(resources);
		identity.SetDestinations(GetDestinations);

		return new ClaimsPrincipal(identity);
	}

	/// <summary>
	/// Constrói o principal do token de ELEVAÇÃO (ADR-0031). Deliberadamente mais pobre que
	/// <see cref="ForUser"/>: só o necessário para ler log de outro tenant, e nada que permita mais.
	/// </summary>
	/// <remarks>
	/// Três ausências são a própria segurança deste token, e cada uma tem teste:
	/// <list type="bullet">
	/// <item><b>Sem <c>tenant_id</c>.</b> É daqui que vem o alcance cross-tenant: o tenant alvo viaja
	/// em <c>X-Tenant-Id</c>, pelo caminho "sem claim → header" que a ADR-0005 já permite.</item>
	/// <item><b>Sem os papéis do próprio usuário</b> — só <see cref="SecureGatePlatform.ElevatedLogReaderRole"/>.
	/// Se os papéis do usuário viessem junto, um <c>admin</c> do tenant A mirando o tenant B faria o
	/// resolvedor consultar <c>(B, admin)</c>; havendo lá um papel com esse nome e permissão de
	/// escrita, o token viraria escrita cross-tenant. Esta é a invariante mais fácil de quebrar sem
	/// perceber, porque <see cref="ForUser"/> copia os papéis e este método não pode.</item>
	/// <item><b>Sem <c>offline_access</c></b>, logo sem refresh token (invariante 2).</item>
	/// </list>
	/// </remarks>
	/// <param name="user">Usuário que elevou.</param>
	/// <param name="resources">Audiences do escopo elevado.</param>
	/// <param name="lifetime">TTL já limitado pelo teto (invariante 3).</param>
	public static ClaimsPrincipal ForElevation(User user, IEnumerable<string> resources, TimeSpan lifetime)
	{
		ArgumentNullException.ThrowIfNull(user);

		var identity = new ClaimsIdentity(
			TokenValidationParameters.DefaultAuthenticationType, Claims.Name, SeccoClaims.Role);

		// A pessoa real: a auditoria no produto continua sendo quem agiu (ADR-0024)
		identity.SetClaim(Claims.Subject, user.Id.ToString());
		identity.SetClaim(Claims.Name, user.UserName);

		identity.SetClaims(SeccoClaims.Role, [SecureGatePlatform.ElevatedLogReaderRole]);
		identity.SetClaim(SecureGatePlatform.TokenExchangeClaim, SecureGatePlatform.ElevationCapability);

		// Revogar a sessão de quem elevou derruba também o token elevado (ADR-0032)
		identity.SetClaim(SeccoClaims.SessionVersion, SessionVersion.From(user.SecurityStamp));

		identity.SetScopes([SecureGatePlatform.ElevatedScope]);
		identity.SetResources(resources);
		identity.SetAccessTokenLifetime(lifetime);

		// Só no access token: não há id_token numa troca, e nada disto é informação de perfil
		identity.SetDestinations(static _ => [Destinations.AccessToken]);

		return new ClaimsPrincipal(identity);
	}

	/// <summary>
	/// Constrói o principal do token de OPERADOR DESIGNADO (ADR-0049): a pessoa real, sem <c>tenant_id</c> (o operador
	/// é tenant-less, ADR-0024), só o escopo <c>securegate:admin</c>, nenhum papel, sem refresh e marcado — o
	/// middleware o reconhece pela marca para reconferir a designação e auditar cada escrita.
	/// </summary>
	/// <param name="user">Pessoa designada.</param>
	/// <param name="resources">Audiences do escopo.</param>
	/// <param name="lifetime">TTL já limitado pelo teto.</param>
	public static ClaimsPrincipal ForDesignatedOperator(User user, IEnumerable<string> resources, TimeSpan lifetime)
	{
		ArgumentNullException.ThrowIfNull(user);

		var identity = new ClaimsIdentity(
			TokenValidationParameters.DefaultAuthenticationType, Claims.Name, SeccoClaims.Role);

		identity.SetClaim(Claims.Subject, user.Id.ToString());
		identity.SetClaim(Claims.Name, string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName);
		identity.SetClaim(SecureGatePlatform.TokenExchangeClaim, SecureGatePlatform.DesignatedOperatorCapability);

		// Revogar a sessão da pessoa derruba também o token de operador (ADR-0032)
		identity.SetClaim(SeccoClaims.SessionVersion, SessionVersion.From(user.SecurityStamp));

		identity.SetScopes([SecureGateScopes.Admin]);
		identity.SetResources(resources);
		identity.SetAccessTokenLifetime(lifetime);
		identity.SetDestinations(static _ => [Destinations.AccessToken]);

		return new ClaimsPrincipal(identity);
	}

	/// <summary>Instante da autenticação (<c>auth_time</c>) de um principal emitido pelo próprio SecureGate.</summary>
	/// <param name="principal">Principal do code ou do refresh token.</param>
	public static DateTimeOffset? AuthenticatedAt(ClaimsPrincipal? principal) =>
		long.TryParse(principal?.GetClaim(Claims.AuthenticationTime), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
			? DateTimeOffset.FromUnixTimeSeconds(seconds)
			: null;

	/// <summary>Métodos de autenticação (<c>amr</c>) de um principal emitido pelo próprio SecureGate.</summary>
	/// <param name="principal">Principal do code ou do refresh token.</param>
	public static IReadOnlyList<string> MethodsOf(ClaimsPrincipal? principal) =>
		principal is null ? [] : [.. principal.GetClaims(Claims.AuthenticationMethodReference)];

	/// <summary>Resolve as audiences (resources) dos scopes de produto via scope manager.</summary>
	/// <param name="scopeManager">Gerenciador de scopes do OpenIddict.</param>
	/// <param name="scopes">Scopes concedidos.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task<List<string>> ResolveResourcesAsync(
		IOpenIddictScopeManager scopeManager,
		IEnumerable<string> scopes,
		CancellationToken cancellationToken)
	{
		var resources = new List<string>();

		await foreach (var resource in scopeManager.ListResourcesAsync([.. scopes], cancellationToken).ConfigureAwait(false))
		{
			resources.Add(resource);
		}

		return resources;
	}

	private static IEnumerable<string> GetDestinations(Claim claim)
	{
		// sver nos dois: o access token para os produtos, o id_token para as aplicações de cookie (ADR-0032)
		if (claim.Type == SeccoClaims.SessionVersion)
		{
			return [Destinations.AccessToken, Destinations.IdentityToken];
		}

		// auth_time e amr nos dois: o id_token é onde o OIDC os define, e a troca de operador designado os lê do
		// access token (ADR-0049)
		if (claim.Type is Claims.AuthenticationTime or Claims.AuthenticationMethodReference)
		{
			return [Destinations.AccessToken, Destinations.IdentityToken];
		}

		// sub e tenant_id: sempre nos dois tokens — o access token precisa do tenant (ADR-0005)
		if (claim.Type is Claims.Subject || claim.Type == SeccoClaims.TenantId)
		{
			return [Destinations.AccessToken, Destinations.IdentityToken];
		}

		// role: sempre no access token (o LogStream resolve permissões dele, ADR-0021);
		// no id_token apenas se o scope 'roles' foi concedido
		if (claim.Type == SeccoClaims.Role)
		{
			return claim.Subject!.HasScope(Scopes.Roles)
				? [Destinations.AccessToken, Destinations.IdentityToken]
				: [Destinations.AccessToken];
		}

		if (claim.Type is Claims.Email)
		{
			return claim.Subject!.HasScope(Scopes.Email)
				? [Destinations.AccessToken, Destinations.IdentityToken]
				: [Destinations.AccessToken];
		}

		if (claim.Type is Claims.Name)
		{
			return claim.Subject!.HasScope(Scopes.Profile)
				? [Destinations.AccessToken, Destinations.IdentityToken]
				: [Destinations.AccessToken];
		}

		return [Destinations.AccessToken];
	}
}
