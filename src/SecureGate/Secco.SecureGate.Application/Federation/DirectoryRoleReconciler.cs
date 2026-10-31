using Secco.SecureGate.Application.Roles;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Application.Tenancy;
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

	/// <summary>Retiradas acima do limite do freio de massa: nada mudou naquele tenant.</summary>
	AbortedByBrake,
}

/// <summary>Resultado de uma reconciliação.</summary>
/// <param name="Outcome">Desfecho.</param>
/// <param name="Added">Perfis acrescentados.</param>
/// <param name="Removed">Perfis retirados.</param>
/// <param name="Blocked">Retiradas barradas pela guarda do último membro ativo.</param>
/// <param name="Unrecorded">Mudanças NÃO feitas porque a trilha não pôde ser gravada (fail-closed).</param>
/// <param name="ErrorCode">Código do erro traduzido do Graph, quando <see cref="Outcome"/> é <c>ReadFailed</c>.</param>
public sealed record DirectorySyncResult(
	DirectorySyncOutcome Outcome,
	int Added = 0,
	int Removed = 0,
	int Blocked = 0,
	int Unrecorded = 0,
	string? ErrorCode = null)
{
	/// <summary>Nada foi feito: tenant fora de alcance.</summary>
	public static readonly DirectorySyncResult Skipped = new(DirectorySyncOutcome.Skipped);
}

/// <summary>
/// Reconciliação de perfis com o diretório federado (ADR-0055, entrega A; ADR-0036). Serviço único, usado pelo
/// login federado (uma conta) e pelo ciclo periódico (o tenant inteiro): dado quem pertence a quais grupos
/// mapeados, acrescenta os perfis que faltam (origem <c>Directory</c>) e retira os de origem <c>Directory</c> que
/// nenhum grupo mapeado justifica mais.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Manual nunca é tocado.</b> Se a atribuição já existe como manual e o grupo também a justifica, fica
/// manual.</item>
/// <item><b>Fail-closed.</b> Leitura do Graph que falha ou vem incompleta: nada muda. Retiradas acima do freio de
/// massa: nada muda no tenant (nem as inclusões).</item>
/// <item><b>Trilha antes da mudança.</b> Cada inclusão/retirada só acontece depois de a trilha local (ADR-0054) ser
/// gravada; falhou a trilha, não muda.</item>
/// <item><b>Último membro ativo.</b> Nunca retira o perfil da última conta ativa que o tem.</item>
/// <item><b>Retirada revoga sessões</b> (ADR-0032) da conta, antes de a atribuição sair.</item>
/// <item><b>Perfil reservado</b> nunca é atribuído, mesmo que um mapeamento aponte para ele; e o tenant de
/// plataforma nunca é reconciliado.</item>
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
	DirectorySyncOptions options)
{
	/// <summary>
	/// Reconcilia UMA conta, no login federado dela: pergunta ao Graph a quais grupos mapeados ela pertence. Sem
	/// mapeamento no tenant não há chamada ao Graph.
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

		return await ApplyAsync(
			tenantId,
			directoryId,
			DirectorySyncTrigger.Login,
			mapped,
			[userId],
			new Dictionary<Guid, IReadOnlySet<Guid>> { [userId] = memberOf },
			cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Reconcilia o tenant inteiro, no ciclo: lê os membros transitivos de cada grupo mapeado e reconcilia as contas
	/// do tenant vinculadas ao diretório. Qualquer leitura que falhe aborta o tenant sem mudar nada.
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

		var membersByGroup = new Dictionary<Guid, HashSet<Guid>>();

		foreach (var groupId in mapped.Select(m => m.EntraGroupId).Distinct())
		{
			var members = await directory.GetGroupMemberIdsAsync(directoryId, groupId, cancellationToken).ConfigureAwait(false);

			if (members.IsFailure)
			{
				return await ReadFailedAsync(tenantId, directoryId, DirectorySyncTrigger.Cycle, members.Error, cancellationToken)
					.ConfigureAwait(false);
			}

			membersByGroup[groupId] = [.. members.Value];
		}

		var accounts = await store.ListLinkedAccountsAsync(tenantId, directoryId, cancellationToken).ConfigureAwait(false);

		if (accounts.Count == 0)
		{
			return new DirectorySyncResult(DirectorySyncOutcome.Completed);
		}

		var groupsByUser = accounts.ToDictionary(
			account => account.UserId,
			account => (IReadOnlySet<Guid>)membersByGroup
				.Where(group => group.Value.Contains(account.ObjectId))
				.Select(group => group.Key)
				.ToHashSet());

		return await ApplyAsync(
			tenantId,
			directoryId,
			DirectorySyncTrigger.Cycle,
			mapped,
			[.. accounts.Select(account => account.UserId)],
			groupsByUser,
			cancellationToken).ConfigureAwait(false);
	}

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

	private async Task<DirectorySyncResult> ApplyAsync(
		Guid tenantId,
		Guid directoryId,
		DirectorySyncTrigger trigger,
		IReadOnlyList<GroupRoleMappingRecord> mapped,
		IReadOnlyList<Guid> userIds,
		IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> groupsByUser,
		CancellationToken cancellationToken)
	{
		var current = (await store.ListAssignmentsAsync(tenantId, userIds, cancellationToken).ConfigureAwait(false))
			.ToLookup(assignment => assignment.UserId);

		var mappedGroups = mapped.Select(m => m.EntraGroupId).ToHashSet();
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

		var removals = plan.Where(change => change.Kind == ChangeKind.Remove).ToList();

		if (removals.Count > 0 && await ExceedsBrakeAsync(tenantId, removals, mappedGroups, cancellationToken).ConfigureAwait(false) is { } total)
		{
			var counted = CountedForBrake(removals, mappedGroups);

			_ = await auditor.RecordAsync(
				new DirectorySyncAuditRecord(
					tenantId, directoryId, DirectorySyncActions.CycleAborted, trigger, Count: counted, Total: total),
				cancellationToken).ConfigureAwait(false);

			return new DirectorySyncResult(DirectorySyncOutcome.AbortedByBrake);
		}

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
