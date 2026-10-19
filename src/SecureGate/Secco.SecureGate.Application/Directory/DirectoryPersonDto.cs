using Secco.SecureGate.Application.Users;

namespace Secco.SecureGate.Application.Directory;

/// <summary>
/// Pessoa do diretório do tenant (ADR-0045). Contrato mínimo de propósito: nada de segundo fator,
/// logins externos, sessões, perfis nem motivo ou data de bloqueio.
/// </summary>
/// <param name="Id">Identificador (o <c>sub</c> dos tokens).</param>
/// <param name="Email">E-mail.</param>
/// <param name="DisplayName">Nome de exibição, opcional.</param>
/// <param name="IsActive">Se a conta está utilizável (nem desativada nem bloqueada).</param>
/// <param name="IsOwner">Se o tenant é o dono da conta; <c>false</c> para quem entra por vínculo.</param>
public sealed record DirectoryPersonDto(Guid Id, string Email, string? DisplayName, bool IsActive, bool IsOwner)
{
	internal static DirectoryPersonDto From(DirectoryPersonData person) =>
		new(
			person.Id,
			person.Email,
			person.DisplayName,
			UserStatuses.From(person.LockoutEnabled, person.LockoutEnd, DateTimeOffset.UtcNow) == UserStatuses.Active,
			person.IsOwner);
}
