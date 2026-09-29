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
/// <param name="DisplayName">Nome de exibição, opcional (#30).</param>
/// <param name="RoleAssignments">
/// Os mesmos nomes de <paramref name="Roles"/>, com a origem de cada atribuição (issue #28) — a
/// tela de gestão usa isto para não deixar o admin tentar remover à mão o que a sincronização de
/// grupo devolveria.
/// </param>
/// <remarks>
/// <paramref name="HasPassword"/>, <paramref name="LocalLoginEnabled"/>, <paramref name="DisplayName"/>
/// e <paramref name="RoleAssignments"/> entram NO FIM: o record é posicional e consumido pelo
/// client NSwag — inserir no meio renumeraria os campos existentes.
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
	bool TwoFactorEnabled,
	string? DisplayName = null,
	IReadOnlyList<RoleAssignmentDto>? RoleAssignments = null);

/// <summary>Um perfil do usuário com a origem da atribuição, para exibição (issue #28, ADR-0036).</summary>
/// <param name="Name">Nome do perfil.</param>
/// <param name="Origin">Origem da atribuição (<c>Manual</c> ou <c>Directory</c>).</param>
/// <param name="SourceGroupId">Id do grupo do Entra ID que originou, quando <see cref="Origin"/> é <c>Directory</c>.</param>
public sealed record RoleAssignmentDto(string Name, RoleAssignmentOrigin Origin, Guid? SourceGroupId);
