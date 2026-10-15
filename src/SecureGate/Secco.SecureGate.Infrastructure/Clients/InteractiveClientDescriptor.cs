using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Infrastructure.Clients;

/// <summary>
/// Como montar, no OpenIddict, um client interativo confidencial (authorization code + PKCE + refresh,
/// consentimento implícito). Compartilhado pelo client da API (ADR-0040) e pela reconciliação de
/// <c>SecureGate:PlatformClients</c> (ADR-0037): as duas origens produzem exatamente o mesmo
/// desenho de permissões.
/// </summary>
internal static class InteractiveClientDescriptor
{
	/// <summary>Acrescenta ao descriptor tudo o que torna o client interativo.</summary>
	/// <param name="descriptor">Descriptor já com <c>ClientId</c>, tipo e nome.</param>
	/// <param name="redirectUris">Redirect URIs (já validadas).</param>
	/// <param name="postLogoutRedirectUris">Post-logout URIs (já validadas).</param>
	public static void Apply(
		OpenIddictApplicationDescriptor descriptor,
		IEnumerable<string> redirectUris,
		IEnumerable<string> postLogoutRedirectUris)
	{
		descriptor.ConsentType = ConsentTypes.Implicit;
		descriptor.Permissions.UnionWith(
		[
			Permissions.Endpoints.Authorization,
			Permissions.Endpoints.EndSession,
			Permissions.GrantTypes.AuthorizationCode,
			Permissions.GrantTypes.RefreshToken,
			Permissions.ResponseTypes.Code,
		]);
		descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
		descriptor.RedirectUris.UnionWith(redirectUris.Select(uri => new Uri(uri)));
		descriptor.PostLogoutRedirectUris.UnionWith(postLogoutRedirectUris.Select(uri => new Uri(uri)));
	}

	/// <summary>Permissão OpenIddict de um escopo (os escopos OIDC com constante própria usam a dela).</summary>
	/// <param name="scope">Nome do escopo.</param>
	public static string ScopePermission(string scope) => scope switch
	{
		Scopes.Email => Permissions.Scopes.Email,
		Scopes.Profile => Permissions.Scopes.Profile,
		Scopes.Roles => Permissions.Scopes.Roles,
		_ => Permissions.Prefixes.Scope + scope,
	};
}
