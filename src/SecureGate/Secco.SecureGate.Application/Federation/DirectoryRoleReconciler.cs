using Secco.SecureGate.Application.Roles;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Application.Users;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Federation;

/// <summary>Desfecho de uma reconciliação (ADR-0055).</summary>
public enum DirectorySyncOutcome
{
	/// <summary>Aplicada (inclusive quando não havia nada a mudar).</summary>
	Completed,

	/// <summary>Não se aplica: tenant de plataforma, ou tenant sem o que reconciliar.</summary>
	Skipped,

	/// <summary>A leitura do Graph falhou ou veio incompleta: nada mudou (fail-closed).</summary>
	ReadFailed,

	/// <summary>Retiradas de perfil ou desativações acima do limite do freio de massa: nada mudou naquele tenant.</summary>
	AbortedByBrake,
}

/// <summary>Resultado de uma reconciliação.</summary>
/// <param name="Outcome">Desfecho.</param>
/// <param name="Added">Perfis acrescentados.</param>
/// <param name="Removed">Perfis retirados.</param>
/// <param name="Blocked">Retiradas barradas pela guarda do último membro ativo.</param>
/// <param name="Unrecorded">Mudanças NÃO feitas porque a trilha não pôde ser gravada (fail-closed).</param>
/// <param name="ErrorCode">Código do erro traduzido do Graph, quando <see cref="Outcome"/> é <c>ReadFailed</c>.</param>
/// <param name="Created">Contas criadas pelo provisionamento (entrega B).</param>
/// <param name="Adopted">Contas adotadas.</param>
/// <param name="Deactivated">Contas desativadas por terem saído do grupo de provisionamento.</param>
/// <param name="Reactivated">Contas reativadas por terem voltado ao grupo.</param>
/// <param name="DeactivationsBlocked">Desativações barradas (conta administrativa ou último membro ativo).</param>
/// <param name="Refused">Pessoas recusadas por e-mail (sem e-mail utilizável, domínio fora da lista, e-mail em uso).</param>
public sealed record DirectorySyncResult(
	DirectorySyncOutcome Outcome,
	int Added = 0,
	int Removed = 0,
	int Blocked = 0,
	int Unrecorded = 0,
	string? ErrorCode = null,
	int Created = 0,
	int Adopted = 0,
	int Deactivated = 0,
	int Reactivated = 0,
	int DeactivationsBlocked = 0,
	int Refused = 0)
{
	/// <summary>Nada foi feito: tenant fora de alcance.</summary>
	public static readonly DirectorySyncResult Skipped = new(DirectorySyncOutcome.Skipped);
}

/// <summary>
/// Reconciliação com o diretório federado (ADR-0055). Serviço único, usado pelo login federado (uma conta) e pelo
/// ciclo periódico (o tenant inteiro). Entrega A: dado quem pertence a quais grupos mapeados, acrescenta os perfis que
/// faltam (origem <c>Directory</c>) e retira os de origem <c>Directory</c> que nenhum grupo mapeado justifica mais.
/// Entrega B: no ciclo, o grupo de provisionamento também cria, adota, desativa e reativa contas
/// (<see cref="DirectoryAccountProvisioner"/>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Manual nunca é tocado.</b> Se a atribuição já existe como manual e o grupo também a justifica, fica
/// manual.</item>
/// <item><b>Fail-closed.</b> Leitura do Graph que falha ou vem incompleta: nada muda. Retiradas de perfil ou
/// desativações de conta acima do freio de massa: nada muda no tenant (nem inclusões, nem criações).</item>
/// <item><b>Trilha antes da mudança.</b> Cada mudança só acontece depois de a trilha local (ADR-0054) ser gravada;
/// falhou a trilha, não muda.</item>
/// <item><b>Último membro ativo.</b> Nunca retira o perfil da última conta ativa que o tem.</item>
/// <item><b>Retirada revoga sessões</b> (ADR-0032) da conta, antes de a atribuição sair.</item>
/// <item><b>Perfil reservado</b> nunca é atribuído, mesmo que um mapeamento aponte para ele; e o tenant de
/// plataforma nunca é reconciliado.</item>
/// <item><b>Sem User.ReadBasic.All</b> o provisionamento se recusa (e registra) e a reconciliação de perfis segue.</item>
/// </list>
/// Cada chamada entra no escopo do tenant (ADR-0041) — nunca em modo plataforma.
/// </remarks>
public sealed class DirectoryRoleReconciler(
	IEntraGroupDirectory directory,
	IGroupRoleMappingRepository mappings,
	IDirectoryRoleStore store,
	IDirectorySyncAuditor auditor,
	ISessionRevoker revoker,
	ITenantAccessScope accessScope,
	DirectorySyncOptions options,
	DirectoryAccountProvisioner provisioner,
	ITenantRepository tenants)
{
	/// <summary>
	/// Reconcilia UMA conta, no login federado dela: pergunta ao Graph a quais grupos mapeados ela pertence. Sem
	/// mapeamento no tenant não há chamada ao Graph. No login só entra e sai perfil: criar e desativar conta é do ciclo
	/// (e a criação no primeiro login é do <see cref="DirectoryAccountProvisioner.ProvisionOnLoginAsync"/>).
	/// </summary>
	/// <param name="tenantId">Tenant dono da conta e da federação.</param>
	/// <param name="directoryId">Directory id da federação.</param>
	/// <param name="userId">Conta que acabou de autenticar.</param>
	/// <param name="objectId">Object id (<c>oid</c>) da conta no diretório.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<DirectorySyncResult> ReconcileLoginAsync(
		Guid tenantId, Guid directoryId, Guid userId, Guid objectId, CancellationToken cancellationToken = default)
	{
		if (tenantId == SecureGatePlatform.TenantId)
		{
			return DirectorySyncResult.Skipped;
		}

		using var scope = accessScope.EnterTenant(tenantId);

		var mapped = await LoadMappingsAsync(tenantId, cancellationToken).ConfigureAwait(false);
		IReadOnlySet<Guid> memberOf = new HashSet<Guid>();

		if (mapped.Count > 0)
		{
			var groupIds = mapped.Select(m => m.EntraGroupId).Distinct().ToList();
			var check = await directory.CheckMemberGroupsAsync(directoryId, objectId, groupIds, cancellationToken)
				.ConfigureAwait(false);

			if (check.IsFailure)
			{
				return await ReadFailedAsync(tenantId, directoryId, DirectorySyncTrigger.Login, check.Error, cancellationToken)
					.ConfigureAwait(false);
			}

			memberOf = check.Value;
		}

		return await ApplyForLoginAsync(
			tenantId,
			directoryId,
			mapped,
			[userId],
			new Dictionary<Guid, IReadOnlySet<Guid>> { [userId] = memberOf },
			cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Reconcilia o tenant inteiro, no ciclo: lê os membros transitivos de cada grupo mapeado e reconcilia as contas
	/// do tenant vinculadas ao diretório; com grupo de provisionamento marcado, também cria, adota, desativa e reativa
	/// contas. Qualquer leitura que falhe aborta o tenant sem mudar nada.
	/// </summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="directoryId">Directory id da federação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<DirectorySyncResult> ReconcileTenantAsync(
		Guid tenantId, Guid directoryId, CancellationToken cancellationToken = default)
	{
		if (tenantId == SecureGatePlatform.TenantId)
		{
			return DirectorySyncResult.Skipped;
		}

		using var scope = accessScope.EnterTenant(tenantId);

		var mapped = await LoadMappingsAsync(tenantId, cancellationToken).ConfigureAwait(false);

		// O grupo de provisionamento é um dos mapeados (ADR-0055): desmarcá-lo desliga criar, adotar, desativar e
		// reativar, e a reconciliação de perfis segue como na entrega A
		var provisioningGroupId = mapped.FirstOrDefault(m => m.IsProvisioningGroup)?.EntraGroupId;

		var membersByGroup = new Dictionary<Guid, HashSet<Guid>>();
		IReadOnlyList<DirectoryPerson>? provisioningPeople = null;

		foreach (var groupId in mapped.Select(m => m.EntraGroupId).Distinct())
		{
			if (groupId == provisioningGroupId)
			{
				// Um passo só: os membros COM o perfil básico (nome e e-mail). Sem o consentimento de leitura de
				// perfil (User.ReadBasic.All) o Graph recusa — o provisionamento se recusa e registra, e os perfis
				// seguem pela leitura só de ids logo abaixo
				var profiled = await directory.GetGroupMembersWithProfileAsync(directoryId, groupId, cancellationToken)
					.ConfigureAwait(false);

				if (profiled.IsSuccess)
				{
					provisioningPeople = profiled.Value;
					membersByGroup[groupId] = [.. profiled.Value.Select(person => person.ObjectId)];

					continue;
				}

				if (profiled.Error.Type != ErrorType.Forbidden)
				{
					return await ReadFailedAsync(tenantId, directoryId, DirectorySyncTrigger.Cycle, profiled.Error, cancellationToken)
						.ConfigureAwait(false);
				}

				_ = await auditor.RecordAsync(
					new DirectorySyncAuditRecord(
						tenantId, directoryId, DirectorySyncActions.ReadFailed, DirectorySyncTrigger.Cycle,
						ErrorCode: profiled.Error.Code, Category: DirectorySyncCategories.Accounts),
					cancellationToken).ConfigureAwait(false);
			}

			var members = await directory.GetGroupMemberIdsAsync(directoryId, groupId, cancellationToken).ConfigureAwait(false);

			if (members.IsFailure)
			{
				return await ReadFailedAsync(tenantId, directoryId, DirectorySyncTrigger.Cycle, members.Error, cancellationToken)
					.ConfigureAwait(false);
			}

			membersByGroup[groupId] = [.. members.Value];
		}

		var accounts = await store.ListLinkedAccountsAsync(tenantId, directoryId, cancellationToken).ConfigureAwait(false);

		if (accounts.Count == 0 && provisioningPeople is null)
		{
			return new DirectorySyncResult(DirectorySyncOutcome.Completed);
		}

		var userIds = accounts.Select(account => account.UserId).ToList();

		var groupsByUser = accounts.ToDictionary(
			account => account.UserId,
			account => (IReadOnlySet<Guid>)GroupsOf(membersByGroup, account.ObjectId));

		// 1) Planejar — nada muda ainda: o freio de massa olha o plano inteiro (perfis E contas) antes de qualquer mudança
		var rolePlan = await PlanAsync(tenantId, mapped, userIds, groupsByUser, cancellationToken).ConfigureAwait(false);
		var accountPlan = ProvisioningPlan.Empty;

		if (provisioningPeople is not null)
		{
			var federation = await tenants.GetFederationAsync(tenantId, cancellationToken).ConfigureAwait(false);

			accountPlan = await provisioner
				.PlanAsync(accounts, provisioningPeople, federation?.GetAllowedEmailDomains() ?? [], cancellationToken)
				.ConfigureAwait(false);
		}

		var mappedGroups = mapped.Select(m => m.EntraGroupId).ToHashSet();
		var removals = rolePlan.Where(change => change.Kind == ChangeKind.Remove).ToList();

		var rolesBase = removals.Count > 0
			? await ExceedsBrakeAsync(tenantId, removals, mappedGroups, cancellationToken).ConfigureAwait(false)
			: null;
		var accountsBase = await provisioner.ExceedsBrakeAsync(tenantId, accountPlan, cancellationToken).ConfigureAwait(false);

		if (rolesBase is not null || accountsBase is not null)
		{
			// Qualquer freio aborta o tenant inteiro: nem cria, nem desativa, nem reativa, nem mexe em perfil
			if (rolesBase is { } rolesTotal)
			{
				_ = await auditor.RecordAsync(
					new DirectorySyncAuditRecord(
						tenantId, directoryId, DirectorySyncActions.CycleAborted, DirectorySyncTrigger.Cycle,
						Count: CountedForBrake(removals, mappedGroups), Total: rolesTotal,
						Category: DirectorySyncCategories.Roles),
					cancellationToken).ConfigureAwait(false);
			}

			if (accountsBase is { } accountsTotal)
			{
				_ = await auditor.RecordAsync(
					new DirectorySyncAuditRecord(
						tenantId, directoryId, DirectorySyncActions.CycleAborted, DirectorySyncTrigger.Cycle,
						Count: accountPlan.Deactivations.Count, Total: accountsTotal,
						Category: DirectorySyncCategories.Accounts),
					cancellationToken).ConfigureAwait(false);
			}

			return new DirectorySyncResult(DirectorySyncOutcome.AbortedByBrake);
		}

		// 2) Contas primeiro: quem acabou de ganhar conta precisa existir para receber os perfis do grupo
		var provisioned = await provisioner.ApplyAsync(tenantId, directoryId, accountPlan, cancellationToken).ConfigureAwait(false);

		if (provisioned.CreatedAccounts.Count > 0)
		{
			foreach (var created in provisioned.CreatedAccounts)
			{
				groupsByUser[created.UserId] = GroupsOf(membersByGroup, created.ObjectId);
			}

			rolePlan.AddRange(await PlanAsync(
				tenantId,
				mapped,
				[.. provisioned.CreatedAccounts.Select(created => created.UserId)],
				groupsByUser,
				cancellationToken).ConfigureAwait(false));
		}

		// 3) Perfis
		var applied = await ExecuteAsync(tenantId, directoryId, DirectorySyncTrigger.Cycle, rolePlan, cancellationToken)
			.ConfigureAwait(false);

		return applied with
		{
			Unrecorded = applied.Unrecorded + provisioned.Unrecorded,
			Created = provisioned.Created,
			Adopted = provisioned.Adopted,
			Deactivated = provisioned.Deactivated,
			Reactivated = provisioned.Reactivated,
			DeactivationsBlocked = provisioned.DeactivationsBlocked,
			Refused = provisioned.Refused,
		};
	}

	private static HashSet<Guid> GroupsOf(Dictionary<Guid, HashSet<Guid>> membersByGroup, Guid objectId) =>
		membersByGroup.Where(group => group.Value.Contains(objectId)).Select(group => group.Key).ToHashSet();

	/// <summary>
	/// Mapeamentos utilizáveis: perfil reservado nunca é atribuído, mesmo que um mapeamento antigo aponte para ele
	/// (a criação já recusa; aqui é a segunda barreira).
	/// </summary>
	private async Task<IReadOnlyList<GroupRoleMappingRecord>> LoadMappingsAsync(
		Guid tenantId, CancellationToken cancellationToken)
	{
		var all = await mappings.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);

		return [.. all.Where(mapping => !RoleInputRules.IsReservedName(mapping.RoleName))];
	}

	private async Task<DirectorySyncResult> ReadFailedAsync(
		Guid tenantId, Guid directoryId, DirectorySyncTrigger trigger, Error error, CancellationToken cancellationToken)
	{
		// Falha de leitura é fato registrado, mas a trilha aqui não é pré-condição de nada (nada vai mudar). Só a do
		// ciclo vai à trilha (uma por tenant por ciclo): a do login fica no log de quem chama, senão uma queda longa do
		// Graph escreveria uma linha por login
		if (trigger == DirectorySyncTrigger.Cycle)
		{
			_ = await auditor.RecordAsync(
				new DirectorySyncAuditRecord(
					tenantId, directoryId, DirectorySyncActions.ReadFailed, trigger, ErrorCode: error.Code),
				cancellationToken).ConfigureAwait(false);
		}

		return new DirectorySyncResult(DirectorySyncOutcome.ReadFailed, ErrorCode: error.Code);
	}

	/// <summary>Login de uma conta: planeja, passa pelo freio de perfis e executa.</summary>
	private async Task<DirectorySyncResult> ApplyForLoginAsync(
		Guid tenantId,
		Guid directoryId,
		IReadOnlyList<GroupRoleMappingRecord> mapped,
		IReadOnlyList<Guid> userIds,
		IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> groupsByUser,
		CancellationToken cancellationToken)
	{
		var plan = await PlanAsync(tenantId, mapped, userIds, groupsByUser, cancellationToken).ConfigureAwait(false);
		var mappedGroups = mapped.Select(m => m.EntraGroupId).ToHashSet();
		var removals = plan.Where(change => change.Kind == ChangeKind.Remove).ToList();

		if (removals.Count > 0
			&& await ExceedsBrakeAsync(tenantId, removals, mappedGroups, cancellationToken).ConfigureAwait(false) is { } total)
		{
			_ = await auditor.RecordAsync(
				new DirectorySyncAuditRecord(
					tenantId,
					directoryId,
					DirectorySyncActions.CycleAborted,
					DirectorySyncTrigger.Login,
					Count: CountedForBrake(removals, mappedGroups),
					Total: total,
					Category: DirectorySyncCategories.Roles),
				cancellationToken).ConfigureAwait(false);

			return new DirectorySyncResult(DirectorySyncOutcome.AbortedByBrake);
		}

		return await ExecuteAsync(tenantId, directoryId, DirectorySyncTrigger.Login, plan, cancellationToken).ConfigureAwait(false);
	}

	private async Task<List<PlannedChange>> PlanAsync(
		Guid tenantId,
		IReadOnlyList<GroupRoleMappingRecord> mapped,
		IReadOnlyList<Guid> userIds,
		IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> groupsByUser,
		CancellationToken cancellationToken)
	{
		var current = (await store.ListAssignmentsAsync(tenantId, userIds, cancellationToken).ConfigureAwait(false))
			.ToLookup(assignment => assignment.UserId);

		var plan = new List<PlannedChange>();

		foreach (var userId in userIds)
		{
			var memberOf = groupsByUser.GetValueOrDefault(userId) ?? new HashSet<Guid>();
			var assignments = current[userId].ToList();

			// Perfil -> grupos mapeados a que a conta pertence e que o concedem
			var justified = mapped
				.Where(m => memberOf.Contains(m.EntraGroupId))
				.GroupBy(m => m.RoleId)
				.ToDictionary(g => g.Key, g => (Name: g.First().RoleName, Groups: g.Select(m => m.EntraGroupId).Order().ToList()));

			foreach (var (roleId, grant) in justified)
			{
				var existing = assignments.FirstOrDefault(a => a.RoleId == roleId);

				if (existing is null)
				{
					plan.Add(new PlannedChange(ChangeKind.Add, userId, roleId, grant.Name, grant.Groups[0]));
				}
				else if (existing.Origin == RoleAssignmentOrigin.Directory
					&& (existing.SourceGroupId is not { } source || !grant.Groups.Contains(source)))
				{
					plan.Add(new PlannedChange(ChangeKind.Retarget, userId, roleId, grant.Name, grant.Groups[0]));
				}

				// Atribuição manual que o grupo também justifica: continua manual, sem ação
			}

			foreach (var assignment in assignments.Where(a => a.Origin == RoleAssignmentOrigin.Directory && !justified.ContainsKey(a.RoleId)))
			{
				plan.Add(new PlannedChange(
					ChangeKind.Remove, userId, assignment.RoleId, assignment.RoleName, assignment.SourceGroupId));
			}
		}

		return plan;
	}

	private async Task<DirectorySyncResult> ExecuteAsync(
		Guid tenantId,
		Guid directoryId,
		DirectorySyncTrigger trigger,
		List<PlannedChange> plan,
		CancellationToken cancellationToken)
	{
		int added = 0, removed = 0, blocked = 0, unrecorded = 0;

		foreach (var userId in plan.Select(change => change.UserId).Distinct())
		{
			var sessionsRevoked = false;

			foreach (var change in plan.Where(change => change.UserId == userId))
			{
				switch (change.Kind)
				{
					case ChangeKind.Add:
						if (!await RecordAsync(tenantId, directoryId, trigger, DirectorySyncActions.RoleAdded, change, cancellationToken)
							.ConfigureAwait(false))
						{
							unrecorded++;
							break;
						}

						if (await store.AddDirectoryAssignmentAsync(tenantId, userId, change.RoleId, change.GroupId!.Value, cancellationToken)
							.ConfigureAwait(false))
						{
							added++;
						}

						break;

					case ChangeKind.Retarget:
						// Só o ponteiro de origem muda: o perfil continua justificado, nenhuma autoridade se move
						_ = await store.RetargetDirectoryAssignmentAsync(
							tenantId, userId, change.RoleId, change.GroupId!.Value, cancellationToken).ConfigureAwait(false);
						break;

					case ChangeKind.Remove:
						if (await store.IsLastActiveMemberAsync(tenantId, change.RoleId, userId, cancellationToken).ConfigureAwait(false))
						{
							// Barrada: a conta mantém o perfil por mais tempo que o devido, nunca o perfil fica vazio
							_ = await RecordAsync(tenantId, directoryId, trigger, DirectorySyncActions.RemovalBlocked, change, cancellationToken)
								.ConfigureAwait(false);
							blocked++;
							break;
						}

						if (!await RecordAsync(tenantId, directoryId, trigger, DirectorySyncActions.RoleRemoved, change, cancellationToken)
							.ConfigureAwait(false))
						{
							unrecorded++;
							break;
						}

						// A sessão cai ANTES de a atribuição sair: se a revogação falhar, nada foi retirado e a
						// próxima passada tenta de novo
						if (!sessionsRevoked)
						{
							await revoker.RevokeAllAsync(userId, SessionRevocationReason.RoleRemoved, cancellationToken)
								.ConfigureAwait(false);
							sessionsRevoked = true;
						}

						if (await store.RemoveDirectoryAssignmentAsync(tenantId, userId, change.RoleId, cancellationToken).ConfigureAwait(false))
						{
							removed++;
						}

						break;
				}
			}
		}

		return new DirectorySyncResult(DirectorySyncOutcome.Completed, added, removed, blocked, unrecorded);
	}

	/// <summary>
	/// Freio de massa (ADR-0055): retiradas acima da fração das atribuições <c>Directory</c> do tenant E acima do
	/// piso. Devolve o total de referência quando o freio dispara; <c>null</c> quando não. Só contam as retiradas de
	/// atribuições cujo grupo AINDA está mapeado: quando o admin remove o mapeamento, desfazer o que ele concedia é
	/// o ato dele, não uma mudança anômala do diretório — senão remover um mapeamento grande travaria o tenant.
	/// </summary>
	private async Task<int?> ExceedsBrakeAsync(
		Guid tenantId, List<PlannedChange> removals, HashSet<Guid> mappedGroups, CancellationToken cancellationToken)
	{
		var counted = CountedForBrake(removals, mappedGroups);

		if (counted <= options.EffectiveMinRemovalsBeforeBrake)
		{
			return null;
		}

		var total = await store.CountDirectoryAssignmentsAsync(tenantId, cancellationToken).ConfigureAwait(false);

		return total > 0 && (double)counted / total > options.EffectiveMaxRemovalRatio ? total : null;
	}

	private static int CountedForBrake(List<PlannedChange> removals, HashSet<Guid> mappedGroups) =>
		removals.Count(removal => removal.GroupId is { } group && mappedGroups.Contains(group));

	private Task<bool> RecordAsync(
		Guid tenantId,
		Guid directoryId,
		DirectorySyncTrigger trigger,
		string action,
		PlannedChange change,
		CancellationToken cancellationToken) =>
		auditor.RecordAsync(
			new DirectorySyncAuditRecord(
				tenantId, directoryId, action, trigger, change.UserId, change.RoleName, change.GroupId),
			cancellationToken);

	private enum ChangeKind
	{
		Add,
		Retarget,
		Remove,
	}

	private sealed record PlannedChange(ChangeKind Kind, Guid UserId, Guid RoleId, string RoleName, Guid? GroupId);
}
