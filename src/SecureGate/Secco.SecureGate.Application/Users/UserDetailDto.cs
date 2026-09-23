namespace Secco.SecureGate.Application.Users;

/// <summary>Usuário detalhado para a gestão — responde "por que fulano tem acesso".</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Email">E-mail.</param>
/// <param name="TenantId">Tenant.</param>
/// <param name="Status">Situação (<see cref="UserStatuses"/>).</param>
/// <param name="LockoutEnd">Fim do bloqueio, só quando <see cref="UserStatuses.LockedOut"/>.</param>
/// <param name="Roles">Perfis.</param>
/// <param name="EffectivePermissions">União das permissões dos perfis, pela resolução dos produtos.</param>
/// <param name="ExternalLogins">Provedores externos vinculados — só o nome, nunca o identificador.</param>
/// <param name="TwoFactorEnabled">Segundo fator ativado (entrega D).</param>
/// <param name="HasPassword">Se a conta já tem senha definida (ADR-0033: nasce sem, até o convite ser aceito).</param>
/// <param name="LocalLoginEnabled">Se a conta aceita login local (usuário/senha, ADR-0033).</param>
/// <remarks>
/// <paramref name="HasPassword"/> e <paramref name="LocalLoginEnabled"/> entram NO FIM: o record é
/// posicional e consumido pelo client NSwag — inserir no meio renumeraria os campos existentes.
/// </remarks>
public sealed record UserDetailDto(
	Guid Id,
	string Email,
	Guid TenantId,
	string Status,
	DateTimeOffset? LockoutEnd,
	IReadOnlyList<string> Roles,
	IReadOnlyList<string> EffectivePermissions,
	IReadOnlyList<string> ExternalLogins,
	bool HasPassword,
	bool LocalLoginEnabled,
	bool TwoFactorEnabled);
