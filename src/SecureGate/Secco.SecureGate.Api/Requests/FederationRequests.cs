namespace Secco.SecureGate.Api.Requests;

/// <summary>Payload de cadastro de um mapeamento grupo→perfil (issue #28).</summary>
/// <param name="EntraGroupId">Id do grupo no Entra ID. Obrigatório.</param>
/// <param name="EntraGroupDisplayName">Nome de exibição do grupo, para snapshot. Obrigatório.</param>
/// <param name="RoleName">Nome do perfil a conceder. Obrigatório.</param>
public sealed record CreateGroupRoleMappingRequest(Guid? EntraGroupId, string? EntraGroupDisplayName, string? RoleName);
