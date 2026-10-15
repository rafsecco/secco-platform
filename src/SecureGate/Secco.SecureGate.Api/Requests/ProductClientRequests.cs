namespace Secco.SecureGate.Api.Requests;

/// <summary>Corpo de criação e de alteração de client de produto (ADR-0037) — conjunto completo.</summary>
/// <param name="Name">Nome legível, único no tenant.</param>
/// <param name="Scopes">Escopos de API de produto.</param>
/// <param name="Roles">Perfis do tenant (só client_credentials).</param>
/// <param name="Type"><c>client_credentials</c> (padrão) ou <c>authorization_code</c> (login de pessoa, ADR-0040). Imutável.</param>
/// <param name="RedirectUris">Redirect URIs (obrigatórias em authorization_code; proibidas em client_credentials).</param>
/// <param name="PostLogoutRedirectUris">Post-logout URIs (opcionais, só authorization_code).</param>
public sealed record ProductClientRequest(
	string? Name, IReadOnlyList<string>? Scopes, IReadOnlyList<string>? Roles,
	string? Type = null, IReadOnlyList<string>? RedirectUris = null, IReadOnlyList<string>? PostLogoutRedirectUris = null);
