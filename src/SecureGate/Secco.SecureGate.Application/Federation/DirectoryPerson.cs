namespace Secco.SecureGate.Application.Federation;

/// <summary>
/// Perfil básico de uma pessoa do diretório federado, lido do Microsoft Graph (ADR-0055). Todo texto aqui é INPUT
/// EXTERNO — vem do diretório do cliente, não do operador: quem o usa valida tamanho, formato e caracteres de
/// controle antes de persistir ou registrar (<see cref="DirectoryAccountRules"/>).
/// </summary>
/// <param name="ObjectId">Object id (<c>oid</c>) da pessoa no diretório.</param>
/// <param name="DisplayName">Nome de exibição, cru.</param>
/// <param name="Mail">Caixa de correio principal (<c>mail</c>), crua; pode ser nula.</param>
/// <param name="UserPrincipalName">UPN, cru; só serve de e-mail se tiver formato de e-mail.</param>
public sealed record DirectoryPerson(Guid ObjectId, string? DisplayName, string? Mail, string? UserPrincipalName);
