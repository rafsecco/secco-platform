using NSubstitute;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Infrastructure.Tenancy;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Montagem dos testes da reconciliação (ADR-0055): tudo em memória, um log único de eventos para provar a ORDEM
/// (trilha antes da mudança, sessão revogada antes de a atribuição sair) e o escopo de tenant em cada chamada.
/// </summary>
internal sealed class DirectoryReconcilerRig
{
	public Guid TenantId { get; } = Guid.CreateVersion7();

	public Guid DirectoryId { get; } = Guid.CreateVersion7();

	/// <summary>Log único, na ordem: <c>audit:&lt;ação&gt;</c>, <c>add</c>, <c>remove</c>, <c>retarget</c>, <c>revoke</c>.</summary>
	public List<string> Events { get; } = [];

	public IEntraGroupDirectory Graph { get; } = Substitute.For<IEntraGroupDirectory>();

	public IGroupRoleMappingRepository Mappings { get; } = Substitute.For<IGroupRoleMappingRepository>();

	public InMemoryDirectoryRoleStore Store { get; }

	public RecordingAuditor Auditor { get; }

	public RecordingRevoker Revoker { get; }

	public TenantAccessScope Scope { get; } = TenantAccessScope.Detached();

	public DirectorySyncOptions Options { get; } = new();

	private readonly List<GroupRoleMappingRecord> _mappings = [];

	public DirectoryReconcilerRig()
	{
		Store = new InMemoryDirectoryRoleStore(Events, Scope);
		Auditor = new RecordingAuditor(Events);
		Revoker = new RecordingRevoker(Events);
		Mappings.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult<IReadOnlyList<GroupRoleMappingRecord>>([.. _mappings]));
	}

	public DirectoryRoleReconciler Build() =>
		new(Graph, Mappings, Store, Auditor, Revoker, Scope, Options);

	/// <summary>Mapeia o grupo ao perfil (cria o perfil se preciso) e devolve o id do perfil.</summary>
	public Guid Map(Guid groupId, string roleName)
	{
		var roleId = Store.RoleId(roleName);
		_mappings.Add(new GroupRoleMappingRecord(Guid.NewGuid(), groupId, "grupo", roleId, roleName, DateTimeOffset.UtcNow));

		return roleId;
	}

	public void Unmap(Guid groupId) => _mappings.RemoveAll(m => m.EntraGroupId == groupId);

	/// <summary>Conta dona do tenant vinculada ao diretório.</summary>
	public (Guid UserId, Guid ObjectId) Account()
	{
		var account = new DirectoryLinkedAccount(Guid.CreateVersion7(), Guid.NewGuid());
		Store.Accounts.Add(account);

		return (account.UserId, account.ObjectId);
	}

	public void GraphMembers(Guid groupId, params Guid[] objectIds) =>
		Graph.GetGroupMemberIdsAsync(DirectoryId, groupId, Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Result.Success<IReadOnlyList<Guid>>([.. objectIds])));

	public void GraphMembersFail(Guid groupId, Error error) =>
		Graph.GetGroupMemberIdsAsync(DirectoryId, groupId, Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Result.Failure<IReadOnlyList<Guid>>(error)));

	public void GraphChecks(Guid objectId, params Guid[] groups) =>
		Graph.CheckMemberGroupsAsync(DirectoryId, objectId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid>(groups))));

	public void GraphChecksFail(Guid objectId, Error error) =>
		Graph.CheckMemberGroupsAsync(DirectoryId, objectId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Result.Failure<IReadOnlySet<Guid>>(error)));
}

internal sealed class InMemoryDirectoryRoleStore(List<string> events, ITenantAccessScope scope) : IDirectoryRoleStore
{
	private readonly Dictionary<string, Guid> _roles = new(StringComparer.OrdinalIgnoreCase);

	public List<DirectoryLinkedAccount> Accounts { get; } = [];

	public List<DirectoryAssignment> Assignments { get; } = [];

	/// <summary>Pares (perfil, conta) em que a conta é o último membro ativo do perfil.</summary>
	public HashSet<(Guid RoleId, Guid UserId)> LastActive { get; } = [];

	/// <summary>Escopo visto em cada chamada: prova que a reconciliação sempre roda no escopo do tenant.</summary>
	public List<(TenantAccessMode Mode, Guid? TenantId)> ScopesSeen { get; } = [];

	public Guid RoleId(string name)
	{
		if (!_roles.TryGetValue(name, out var id))
		{
			id = Guid.CreateVersion7();
			_roles[name] = id;
		}

		return id;
	}

	public void Has(Guid userId, string roleName, RoleAssignmentOrigin origin, Guid? sourceGroupId = null) =>
		Assignments.Add(new DirectoryAssignment(userId, RoleId(roleName), roleName, origin, sourceGroupId));

	public DirectoryAssignment? Find(Guid userId, string roleName) =>
		Assignments.FirstOrDefault(a => a.UserId == userId && a.RoleId == RoleId(roleName));

	private void Seen() => ScopesSeen.Add((scope.Mode, scope.TenantId));

	public Task<IReadOnlyList<DirectoryLinkedAccount>> ListLinkedAccountsAsync(
		Guid tenantId, Guid directoryId, CancellationToken cancellationToken = default)
	{
		Seen();

		return Task.FromResult<IReadOnlyList<DirectoryLinkedAccount>>([.. Accounts]);
	}

	public Task<IReadOnlyList<DirectoryAssignment>> ListAssignmentsAsync(
		Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
	{
		Seen();

		return Task.FromResult<IReadOnlyList<DirectoryAssignment>>([.. Assignments.Where(a => userIds.Contains(a.UserId))]);
	}

	public Task<int> CountDirectoryAssignmentsAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		Seen();

		return Task.FromResult(Assignments.Count(a => a.Origin == RoleAssignmentOrigin.Directory));
	}

	public Task<bool> AddDirectoryAssignmentAsync(
		Guid tenantId, Guid userId, Guid roleId, Guid sourceGroupId, CancellationToken cancellationToken = default)
	{
		Seen();
		events.Add("add");

		if (Assignments.Any(a => a.UserId == userId && a.RoleId == roleId))
		{
			return Task.FromResult(false);
		}

		var name = _roles.First(r => r.Value == roleId).Key;
		Assignments.Add(new DirectoryAssignment(userId, roleId, name, RoleAssignmentOrigin.Directory, sourceGroupId));

		return Task.FromResult(true);
	}

	public Task<bool> RemoveDirectoryAssignmentAsync(
		Guid tenantId, Guid userId, Guid roleId, CancellationToken cancellationToken = default)
	{
		Seen();
		events.Add("remove");

		// Como o contrato: só a de origem Directory sai
		return Task.FromResult(Assignments.RemoveAll(
			a => a.UserId == userId && a.RoleId == roleId && a.Origin == RoleAssignmentOrigin.Directory) > 0);
	}

	public Task<bool> RetargetDirectoryAssignmentAsync(
		Guid tenantId, Guid userId, Guid roleId, Guid sourceGroupId, CancellationToken cancellationToken = default)
	{
		Seen();
		events.Add("retarget");

		var index = Assignments.FindIndex(a => a.UserId == userId && a.RoleId == roleId && a.Origin == RoleAssignmentOrigin.Directory);

		if (index < 0)
		{
			return Task.FromResult(false);
		}

		Assignments[index] = Assignments[index] with { SourceGroupId = sourceGroupId };

		return Task.FromResult(true);
	}

	public Task<bool> IsLastActiveMemberAsync(
		Guid tenantId, Guid roleId, Guid userId, CancellationToken cancellationToken = default)
	{
		Seen();

		return Task.FromResult(LastActive.Contains((roleId, userId)));
	}
}

internal sealed class RecordingAuditor(List<string> events) : IDirectorySyncAuditor
{
	public List<DirectorySyncAuditRecord> Records { get; } = [];

	/// <summary>Decide se a trilha aceita o registro; padrão: aceita tudo.</summary>
	public Func<DirectorySyncAuditRecord, bool> Accept { get; set; } = _ => true;

	public Task<bool> RecordAsync(DirectorySyncAuditRecord record, CancellationToken cancellationToken = default)
	{
		events.Add("audit:" + record.Action[DirectorySyncActions.Prefix.Length..]);
		Records.Add(record);

		return Task.FromResult(Accept(record));
	}
}

internal sealed class RecordingRevoker(List<string> events) : ISessionRevoker
{
	public List<(Guid UserId, SessionRevocationReason Reason)> Revoked { get; } = [];

	public Task RevokeAllAsync(Guid userId, SessionRevocationReason reason, CancellationToken cancellationToken = default)
	{
		events.Add("revoke");
		Revoked.Add((userId, reason));

		return Task.CompletedTask;
	}
}
