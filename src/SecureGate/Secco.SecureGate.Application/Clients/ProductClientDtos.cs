namespace Secco.SecureGate.Application.Clients;

/// <summary>Client de produto como a API o expõe — nunca com o secret (ADR-0037).</summary>
public sealed record ProductClientDto(
	string ClientId, string Name, IReadOnlyList<string> Scopes, IReadOnlyList<string> Roles, DateTimeOffset? CreatedAt,
	string? Type = null, IReadOnlyList<string>? RedirectUris = null, IReadOnlyList<string>? PostLogoutRedirectUris = null);

/// <summary>Resposta da criação: o único momento, junto da rotação, em que o secret aparece.</summary>
public sealed record CreatedProductClientDto(
	string ClientId, string Name, IReadOnlyList<string> Scopes, IReadOnlyList<string> Roles, DateTimeOffset? CreatedAt,
	string ClientSecret,
	string? Type = null, IReadOnlyList<string>? RedirectUris = null, IReadOnlyList<string>? PostLogoutRedirectUris = null);

/// <summary>Resposta da rotação de secret.</summary>
public sealed record ProductClientSecretDto(string ClientId, string ClientSecret);

/// <summary>Entrada de criação e de alteração de acesso (conjunto completo).</summary>
/// <remarks>
/// <c>Type</c> nulo = <c>client_credentials</c>. <c>authorization_code</c> (ADR-0040) cria um client
/// interativo confidencial, com PKCE, vinculado ao tenant, e exige <c>RedirectUris</c>.
/// </remarks>
public sealed record ProductClientCommand(
	string? Name, IReadOnlyList<string>? Scopes, IReadOnlyList<string>? Roles,
	string? Type = null, IReadOnlyList<string>? RedirectUris = null, IReadOnlyList<string>? PostLogoutRedirectUris = null);

/// <summary>Acesso já validado: nome aparado, escopos de produto, papéis com o nome canônico do tenant.</summary>
public sealed record ProductClientAccess(
	string Name, IReadOnlyList<string> Scopes, IReadOnlyList<string> Roles,
	string Type = ProductClientRules.ClientCredentialsType,
	IReadOnlyList<string>? RedirectUris = null, IReadOnlyList<string>? PostLogoutRedirectUris = null);
