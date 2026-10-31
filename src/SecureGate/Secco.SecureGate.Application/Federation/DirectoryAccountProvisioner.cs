using Secco.SecureGate.Application.Roles;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Application.Users;

namespace Secco.SecureGate.Application.Federation;

/// <summary>Pessoa do grupo de provisionamento sem conta, com o rascunho já validado.</summary>
/// <param name="Person">Pessoa lida do Graph.</param>
/// <param name="Draft">E-mail e nome validados.</param>
public sealed record PlannedCreation(DirectoryPerson Person, DirectoryAccountDraft Draft);

/// <summary>Pessoa do grupo de provisionamento recusada antes de existir conta.</summary>
/// <param name="ObjectId">Object id da pessoa no diretório (o e-mail dela nunca vai à trilha).</param>
/// <param name="Reason">Motivo.</param>
public sealed record PlannedRefusal(Guid ObjectId, EmailRefusalReason Reason);

/// <summary>
/// O que o provisionamento pretende fazer no tenant, calculado sem mudar nada (ADR-0055): o freio de massa olha o
/// plano antes de qualquer mudança.
/// </summary>
/// <param name="Creations">Contas a criar.</param>
/// <param name="Refusals">Pessoas recusadas (sem e-mail utilizável ou domínio fora da lista).</param>
/// <param name="Adoptions">Contas existentes, vinculadas por <c>oid</c> e presentes no grupo, a marcar como geridas.</param>
/// <param name="Reactivations">Contas geridas, desativadas pelo diretório, de volta ao grupo.</param>
/// <param name="Deactivations">Contas geridas, ativas, que não estão mais no grupo.</param>
public sealed record ProvisioningPlan(
	IReadOnlyList<PlannedCreation> Creations,
	IReadOnlyList<PlannedRefusal> Refusals,
	IReadOnlyList<DirectoryLinkedAccount> Adoptions,
	IReadOnlyList<DirectoryLinkedAccount> Reactivations,
	IReadOnlyList<DirectoryLinkedAccount> Deactivations)
{
	/// <summary>Plano vazio: o tenant não tem provisionamento ativo neste ciclo.</summary>
	public static readonly ProvisioningPlan Empty = new([], [], [], [], []);
}

/// <summary>O que o provisionamento fez.</summary>
/// <param name="Created">Contas criadas.</param>
/// <param name="Adopted">Contas adotadas.</param>
/// <param name="Deactivated">Contas desativadas.</param>
/// <param name="Reactivated">Contas reativadas.</param>
/// <param name="DeactivationsBlocked">Desativações barradas (conta administrativa ou último membro ativo).</param>
/// <param name="Refused">Pessoas recusadas (e-mail).</param>
/// <param name="Unrecorded">Mudanças NÃO feitas porque a trilha não pôde ser gravada (fail-closed).</param>
/// <param name="CreatedAccounts">As contas criadas, para a reconciliação de perfis alcançá-las no mesmo ciclo.</param>
public sealed record ProvisioningOutcome(
	int Created,
	int Adopted,
	int Deactivated,
	int Reactivated,
	int DeactivationsBlocked,
	int Refused,
	int Unrecorded,
	IReadOnlyList<DirectoryLinkedAccount> CreatedAccounts)
{
	/// <summary>Nada foi feito.</summary>
	public static readonly ProvisioningOutcome None = new(0, 0, 0, 0, 0, 0, 0, []);
}

/// <summary>Desfecho do provisionamento no primeiro login (JIT).</summary>
public enum LoginProvisioningKind
{
	/// <summary>A conta foi criada; o login segue.</summary>
	Created,

	/// <summary>A pessoa não está em nenhum grupo de provisionamento deste diretório (resposta definitiva).</summary>
	NotProvisioned,

	/// <summary>A pessoa está no grupo, mas o e-mail foi recusado (resposta definitiva; foi à trilha).</summary>
	Refused,

	/// <summary>Falha de leitura ou de trilha: nada foi criado, e a resposta não é definitiva.</summary>
	Failed,
}

/// <summary>Resultado do provisionamento no primeiro login.</summary>
/// <param name="Kind">Desfecho.</param>
/// <param name="UserId">A conta criada, quando <see cref="Kind"/> é <c>Created</c>.</param>
public sealed record LoginProvisioningResult(LoginProvisioningKind Kind, Guid? UserId = null)
{
	/// <summary>Resposta definitiva de que a pessoa não ganha conta agora (pode ir ao cache negativo).</summary>
	public bool IsDefinitiveRefusal => Kind is LoginProvisioningKind.NotProvisioned or LoginProvisioningKind.Refused;
}

/// <summary>
/// Provisionamento de contas pelo diretório federado (ADR-0055, entrega B): cria a conta de quem entra no grupo de
/// provisionamento, adota a pré-existente, desativa a gerida que saiu e reativa a que voltou.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Só a conta gerida é desativada ou reativada.</b> A conta só local nunca é tocada; a administrativa
/// (operador, admin com concessão, operador designado — ADR-0044/0049) nunca é adotada nem desativada.</item>
/// <item><b>Reativação só desfaz o que a sincronização fez:</b> origem <c>Directory</c>. A <c>Manual</c> é decisão
/// humana.</item>
/// <item><b>Desativar nunca esvazia um perfil</b> (guarda do último membro, ADR-0036 generalizada) e revoga as sessões
/// e os refresh tokens ANTES do lockout (ADR-0032).</item>
/// <item><b>Trilha antes da mudança</b> (ADR-0054): sem registro, sem mudança.</item>
/// <item><b>E-mail:</b> do Graph, validado, num domínio permitido; colisão com QUALQUER conta da instalação recusa.</item>
/// </list>
/// Quem chama já entrou no escopo do tenant (ADR-0041). O ciclo planeja, passa pelo freio de massa e só então aplica.
/// </remarks>
public sealed class DirectoryAccountProvisioner(
	IDirectoryAccountStore accounts,
	IDirectoryRoleStore roles,
	IDirectorySyncAuditor auditor,
	ISessionRevoker revoker,
	ITenantAdminGrantRepository grants,
	IEntraGroupDirectory directory,
	ITenantAccessScope accessScope,
	DirectorySyncOptions options)
{
	/// <summary>
	/// Calcula o que fazer, sem mudar nada. <paramref name="people"/> é a leitura COMPLETA do grupo de provisionamento
	/// (quem falhou ou veio incompleto nunca chega aqui).
	/// </summary>
	/// <param name="linked">Contas donas do tenant vinculadas ao diretório.</param>
	/// <param name="people">Membros transitivos do grupo de provisionamento, com perfil.</param>
	/// <param name="allowedDomains">Domínios de e-mail permitidos da federação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<ProvisioningPlan> PlanAsync(
		IReadOnlyList<DirectoryLinkedAccount> linked,
		IReadOnlyList<DirectoryPerson> people,
		IReadOnlyCollection<string> allowedDomains,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(linked);
		ArgumentNullException.ThrowIfNull(people);
		ArgumentNullException.ThrowIfNull(allowedDomains);

		var byObject = linked.ToDictionary(account => account.ObjectId);
		var inGroup = people.Select(person => person.ObjectId).ToHashSet();

		var creations = new List<PlannedCreation>();
		var refusals = new List<PlannedRefusal>();
		var adoptions = new List<DirectoryLinkedAccount>();
		var reactivations = new List<DirectoryLinkedAccount>();

		foreach (var person in people)
		{
			if (!byObject.TryGetValue(person.ObjectId, out var account))
			{
				if (DirectoryAccountRules.TryBuildDraft(person, allowedDomains, out var draft, out var refusal))
				{
					creations.Add(new PlannedCreation(person, draft!));
				}
				else
				{
					refusals.Add(new PlannedRefusal(person.ObjectId, refusal));
				}

				continue;
			}

			if (!account.DirectoryManaged)
			{
				// Adoção: a conta já está vinculada por oid a este diretório e apareceu no grupo. A administrativa
				// fica fora — o diretório do cliente não passa a mandar em quem opera a instalação
				if (!await grants.IsAdministrativeAccountAsync(account.UserId, cancellationToken).ConfigureAwait(false))
				{
					adoptions.Add(account);
				}
			}
			else if (account.IsDeactivated && account.DeactivationOrigin == DeactivationOrigin.Directory)
			{
				reactivations.Add(account);
			}
		}

		var deactivations = linked
			.Where(account => account.DirectoryManaged && !account.IsDeactivated && !inGroup.Contains(account.ObjectId))
			.ToList();

		return new ProvisioningPlan(creations, refusals, adoptions, reactivations, deactivations);
	}

	/// <summary>
	/// Freio de massa das desativações (ADR-0055): mais desativações que o piso E que a fração das contas geridas do
	/// tenant. Uma lista de membros vazia ou truncada pela Microsoft não pode derrubar a empresa.
	/// </summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="plan">Plano.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>O total de referência quando o freio dispara; <c>null</c> quando não.</returns>
	public async Task<int?> ExceedsBrakeAsync(Guid tenantId, ProvisioningPlan plan, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plan);

		var counted = plan.Deactivations.Count;

		if (counted <= options.EffectiveMinRemovalsBeforeBrake)
		{
			return null;
		}

		var total = await accounts.CountManagedAsync(tenantId, cancellationToken).ConfigureAwait(false);

		return total > 0 && (double)counted / total > options.EffectiveMaxRemovalRatio ? total : null;
	}

	/// <summary>Aplica o plano, mudança a mudança, cada uma só depois de a trilha ser gravada.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="directoryId">Directory id da federação.</param>
	/// <param name="plan">Plano aprovado pelo freio.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<ProvisioningOutcome> ApplyAsync(
		Guid tenantId, Guid directoryId, ProvisioningPlan plan, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plan);

		int created = 0, adopted = 0, deactivated = 0, reactivated = 0, blocked = 0, refused = 0, unrecorded = 0;
		var createdAccounts = new List<DirectoryLinkedAccount>();

		foreach (var refusal in plan.Refusals)
		{
			// Fato registrado, mas a trilha aqui não é pré-condição de nada: nenhuma mudança depende dela
			_ = await RecordRefusalAsync(tenantId, directoryId, DirectorySyncTrigger.Cycle, refusal.ObjectId, refusal.Reason, cancellationToken)
				.ConfigureAwait(false);
			refused++;
		}

		foreach (var account in plan.Adoptions)
		{
			if (!await RecordAsync(
				new DirectorySyncAuditRecord(
					tenantId, directoryId, DirectorySyncActions.AccountAdopted, DirectorySyncTrigger.Cycle, account.UserId,
					ObjectId: account.ObjectId, Category: DirectorySyncCategories.Accounts),
				cancellationToken).ConfigureAwait(false))
			{
				unrecorded++;
				continue;
			}

			if (await accounts.AdoptAsync(tenantId, account.UserId, cancellationToken).ConfigureAwait(false))
			{
				adopted++;
			}
		}

		foreach (var account in plan.Reactivations)
		{
			if (!await RecordAsync(
				new DirectorySyncAuditRecord(
					tenantId, directoryId, DirectorySyncActions.AccountReactivated, DirectorySyncTrigger.Cycle, account.UserId,
					ObjectId: account.ObjectId, Category: DirectorySyncCategories.Accounts),
				cancellationToken).ConfigureAwait(false))
			{
				unrecorded++;
				continue;
			}

			if (await accounts.ReactivateAsync(tenantId, account.UserId, cancellationToken).ConfigureAwait(false))
			{
				reactivated++;
			}
		}

		foreach (var creation in plan.Creations)
		{
			var result = await CreateAsync(tenantId, directoryId, DirectorySyncTrigger.Cycle, creation.Person, creation.Draft, cancellationToken)
				.ConfigureAwait(false);

			switch (result.Kind)
			{
				case LoginProvisioningKind.Created:
					created++;
					createdAccounts.Add(new DirectoryLinkedAccount(
						result.UserId!.Value, creation.Person.ObjectId, DirectoryManaged: true));
					break;
				case LoginProvisioningKind.Refused:
					refused++;
					break;
				default:
					unrecorded++;
					break;
			}
		}

		foreach (var account in plan.Deactivations)
		{
			switch (await DeactivateAsync(tenantId, directoryId, account, cancellationToken).ConfigureAwait(false))
			{
				case DeactivationResult.Done:
					deactivated++;
					break;
				case DeactivationResult.Blocked:
					blocked++;
					break;
				case DeactivationResult.Unrecorded:
					unrecorded++;
					break;
			}
		}

		return new ProvisioningOutcome(
			created, adopted, deactivated, reactivated, blocked, refused, unrecorded, createdAccounts);
	}

	/// <summary>
	/// Primeiro login (JIT) de uma pessoa sem conta: procura, em ordem determinística, o primeiro tenant deste
	/// diretório cujo grupo de provisionamento a contém, e cria a conta nele. Qualquer falha ou ausência recusa —
	/// quem chama devolve a recusa genérica, sem revelar o motivo.
	/// </summary>
	/// <param name="directoryId">Directory id (<c>tid</c>) do login.</param>
	/// <param name="objectId">Object id (<c>oid</c>) de quem está entrando.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<LoginProvisioningResult> ProvisionOnLoginAsync(
		Guid directoryId, Guid objectId, CancellationToken cancellationToken = default)
	{
		var targets = await accounts.ListProvisioningTargetsAsync(directoryId, cancellationToken).ConfigureAwait(false);

		// Mapeamento para perfil reservado nunca vale (a mesma barreira do reconciliador)
		foreach (var target in targets.Where(target => !RoleInputRules.IsReservedName(target.RoleName)))
		{
			var check = await directory.CheckMemberGroupsAsync(directoryId, objectId, [target.GroupId], cancellationToken)
				.ConfigureAwait(false);

			// Falha de leitura não adivinha: recusa agora, sem decidir por um tenant que não se conseguiu conferir
			if (check.IsFailure)
			{
				return new LoginProvisioningResult(LoginProvisioningKind.Failed);
			}

			if (!check.Value.Contains(target.GroupId))
			{
				continue;
			}

			return await ProvisionInTenantAsync(directoryId, objectId, target, cancellationToken).ConfigureAwait(false);
		}

		return new LoginProvisioningResult(LoginProvisioningKind.NotProvisioned);
	}

	private async Task<LoginProvisioningResult> ProvisionInTenantAsync(
		Guid directoryId, Guid objectId, ProvisioningTarget target, CancellationToken cancellationToken)
	{
		using var scope = accessScope.EnterTenant(target.TenantId);

		var profile = await directory.GetPersonAsync(directoryId, objectId, cancellationToken).ConfigureAwait(false);

		if (profile.IsFailure)
		{
			// Sem User.ReadBasic.All (ou Graph fora): o provisionamento recusa; no login não há trilha de leitura
			return new LoginProvisioningResult(LoginProvisioningKind.Failed);
		}

		if (!DirectoryAccountRules.TryBuildDraft(profile.Value, target.AllowedEmailDomains, out var draft, out var refusal))
		{
			_ = await RecordRefusalAsync(target.TenantId, directoryId, DirectorySyncTrigger.Login, objectId, refusal, cancellationToken)
				.ConfigureAwait(false);

			return new LoginProvisioningResult(LoginProvisioningKind.Refused);
		}

		return await CreateAsync(target.TenantId, directoryId, DirectorySyncTrigger.Login, profile.Value, draft!, cancellationToken)
			.ConfigureAwait(false);
	}

	private async Task<LoginProvisioningResult> CreateAsync(
		Guid tenantId,
		Guid directoryId,
		DirectorySyncTrigger trigger,
		DirectoryPerson person,
		DirectoryAccountDraft draft,
		CancellationToken cancellationToken)
	{
		// O e-mail é único na instalação: a colisão é recusa registrada, nunca criação de uma segunda conta
		if (await accounts.EmailInUseAsync(draft.Email, cancellationToken).ConfigureAwait(false))
		{
			_ = await RecordRefusalAsync(tenantId, directoryId, trigger, person.ObjectId, EmailRefusalReason.EmailInUse, cancellationToken)
				.ConfigureAwait(false);

			return new LoginProvisioningResult(LoginProvisioningKind.Refused);
		}

		var userId = Guid.CreateVersion7();

		// Trilha ANTES da conta (ADR-0054): a entrada já aponta para o id que a conta vai ter
		if (!await RecordAsync(
			new DirectorySyncAuditRecord(
				tenantId, directoryId, DirectorySyncActions.AccountCreated, trigger, userId,
				ObjectId: person.ObjectId, Category: DirectorySyncCategories.Accounts),
			cancellationToken).ConfigureAwait(false))
		{
			return new LoginProvisioningResult(LoginProvisioningKind.Failed);
		}

		var outcome = await accounts.CreateAsync(tenantId, directoryId, userId, person.ObjectId, draft, cancellationToken)
			.ConfigureAwait(false);

		if (outcome == CreateDirectoryAccountOutcome.EmailInUse)
		{
			// Corrida: o e-mail apareceu entre a checagem e o INSERT. A trilha mostra as duas coisas, honestamente
			_ = await RecordRefusalAsync(tenantId, directoryId, trigger, person.ObjectId, EmailRefusalReason.EmailInUse, cancellationToken)
				.ConfigureAwait(false);

			return new LoginProvisioningResult(LoginProvisioningKind.Refused);
		}

		return new LoginProvisioningResult(LoginProvisioningKind.Created, userId);
	}

	private enum DeactivationResult
	{
		Done,
		Blocked,
		Unrecorded,
	}

	private async Task<DeactivationResult> DeactivateAsync(
		Guid tenantId, Guid directoryId, DirectoryLinkedAccount account, CancellationToken cancellationToken)
	{
		// Conta administrativa nunca é desativada pelo diretório do cliente (ADR-0044/0049/0055). A marca de
		// "gerida" não a protege se a conta virou administrativa depois
		if (await grants.IsAdministrativeAccountAsync(account.UserId, cancellationToken).ConfigureAwait(false))
		{
			_ = await RecordBlockedAsync(tenantId, directoryId, account, "administrative-account", roleName: null, cancellationToken)
				.ConfigureAwait(false);

			return DeactivationResult.Blocked;
		}

		// Guarda do último membro ativo (ADR-0036 generalizada): desativar a conta esvaziaria algum perfil
		var assignments = await roles.ListAssignmentsAsync(tenantId, [account.UserId], cancellationToken).ConfigureAwait(false);

		foreach (var assignment in assignments)
		{
			if (await roles.IsLastActiveMemberAsync(tenantId, assignment.RoleId, account.UserId, cancellationToken).ConfigureAwait(false))
			{
				_ = await RecordBlockedAsync(
					tenantId, directoryId, account, "last-active-member", assignment.RoleName, cancellationToken)
					.ConfigureAwait(false);

				return DeactivationResult.Blocked;
			}
		}

		if (!await RecordAsync(
			new DirectorySyncAuditRecord(
				tenantId, directoryId, DirectorySyncActions.AccountDeactivated, DirectorySyncTrigger.Cycle, account.UserId,
				ObjectId: account.ObjectId, Category: DirectorySyncCategories.Accounts),
			cancellationToken).ConfigureAwait(false))
		{
			return DeactivationResult.Unrecorded;
		}

		// A sessão cai ANTES do lockout (ADR-0032), como na retirada de perfil: se a revogação falhar, nada foi
		// desativado e a próxima passada tenta de novo. A conta desativada não renova token (a guarda re-checa o estado)
		await revoker.RevokeAllAsync(account.UserId, SessionRevocationReason.UserDeactivated, cancellationToken)
			.ConfigureAwait(false);

		return await accounts.DeactivateAsync(tenantId, account.UserId, cancellationToken).ConfigureAwait(false)
			? DeactivationResult.Done
			: DeactivationResult.Blocked;
	}

	private Task<bool> RecordBlockedAsync(
		Guid tenantId,
		Guid directoryId,
		DirectoryLinkedAccount account,
		string reason,
		string? roleName,
		CancellationToken cancellationToken) =>
		RecordAsync(
			new DirectorySyncAuditRecord(
				tenantId, directoryId, DirectorySyncActions.DeactivationBlocked, DirectorySyncTrigger.Cycle, account.UserId,
				RoleName: roleName, Reason: reason, ObjectId: account.ObjectId, Category: DirectorySyncCategories.Accounts),
			cancellationToken);

	private Task<bool> RecordRefusalAsync(
		Guid tenantId,
		Guid directoryId,
		DirectorySyncTrigger trigger,
		Guid objectId,
		EmailRefusalReason reason,
		CancellationToken cancellationToken) =>
		RecordAsync(
			new DirectorySyncAuditRecord(
				tenantId, directoryId, DirectorySyncActions.EmailRefused, trigger,
				Reason: EmailRefusalReasons.From(reason), ObjectId: objectId, Category: DirectorySyncCategories.Accounts),
			cancellationToken);

	private Task<bool> RecordAsync(DirectorySyncAuditRecord record, CancellationToken cancellationToken) =>
		auditor.RecordAsync(record, cancellationToken);
}
