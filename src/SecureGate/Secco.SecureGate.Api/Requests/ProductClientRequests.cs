namespace Secco.SecureGate.Api.Requests;

/// <summary>Corpo de criação e de alteração de client de produto (ADR-0037) — conjunto completo.</summary>
/// <param name="Name">Nome legível, único no tenant.</param>
/// <param name="Scopes">Escopos de API de produto.</param>
/// <param name="Roles">Perfis do tenant.</param>
public sealed record ProductClientRequest(string? Name, IReadOnlyList<string>? Scopes, IReadOnlyList<string>? Roles);
