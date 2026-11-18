using Secco.SecureGate.Application.Users;

namespace Secco.SecureGate.Application.Directory;

/// <summary>
/// Pessoa do diretório do tenant (ADR-0045). Contrato mínimo de propósito: identidade, os atributos
/// do diretório (cargo, departamento, telefone comercial, gestor) e a origem deles — nada de segundo
/// fator, logins externos, sessões, perfis nem motivo ou data de bloqueio.
/// </summary>
/// <param name="Id">Identificador (o <c>sub</c> dos tokens).</param>
/// <param name="Email">E-mail.</param>
/// <param name="DisplayName">Nome de exibição, opcional.</param>
/// <param name="IsActive">Se a conta está utilizável (nem desativada nem bloqueada).</param>
/// <param name="IsOwner">Se o tenant é o dono da conta; <c>false</c> para quem entra por vínculo.</param>
/// <param name="JobTitle">Cargo, opcional.</param>
/// <param name="Department">Departamento, opcional.</param>
/// <param name="BusinessPhone">Telefone comercial, opcional.</param>
/// <param name="ManagerId">Gestor; nulo se não há ou se ele não está no diretório deste tenant.</param>
/// <param name="AttributesSource"><c>"directory"</c> quando os atributos vêm do diretório federado; nulo caso contrário.</param>
public sealed record DirectoryPersonDto(
	Guid Id, string Email, string? DisplayName, bool IsActive, bool IsOwner,
	string? JobTitle, string? Department, string? BusinessPhone, Guid? ManagerId, string? AttributesSource)
{
	/// <summary>Valor de <see cref="AttributesSource"/> para atributos sincronizados do diretório.</summary>
	public const string DirectorySource = "directory";

	internal static DirectoryPersonDto From(DirectoryPersonData person) =>
		new(
			person.Id,
			person.Email,
			person.DisplayName,
			UserStatuses.From(person.LockoutEnabled, person.LockoutEnd, DateTimeOffset.UtcNow) == UserStatuses.Active,
			person.IsOwner,
			person.JobTitle,
			person.Department,
			person.BusinessPhone,
			person.ManagerId,
			person.AttributesOrigin == DirectoryAttributesOrigin.Directory ? DirectorySource : null);
}
