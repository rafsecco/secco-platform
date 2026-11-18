using Secco.SecureGate.Application.Users;

namespace Secco.SecureGate.Application.Federation;

/// <summary>Motivo de um aviso do planejamento dos atributos do diretório.</summary>
public enum DirectoryAttributeWarning
{
	/// <summary>O gestor do diretório não tem conta vinculada no tenant.</summary>
	ManagerNotFound,

	/// <summary>O gestor tem conta, mas ela está inativa.</summary>
	ManagerInactive,

	/// <summary>O diretório aponta a pessoa como gestora dela mesma.</summary>
	ManagerSelf,

	/// <summary>O gestor fecharia um ciclo de gestão.</summary>
	ManagerCycle,

	/// <summary>Algum texto foi descartado (controle ou acima do limite).</summary>
	FieldDiscarded,
}

/// <summary>Aviso por conta; nunca carrega conteúdo do diretório (ADR-0020).</summary>
/// <param name="UserId">Conta no SecureGate.</param>
/// <param name="Warning">Motivo.</param>
public sealed record DirectoryAttributeNotice(Guid UserId, DirectoryAttributeWarning Warning);

/// <summary>Resultado do planejamento.</summary>
/// <param name="Updates">Contas a gravar.</param>
/// <param name="Notices">Avisos, ordenados por conta e motivo.</param>
/// <param name="Cleared">Contas que perderiam valor por leitura vazia do diretório (alimenta o freio).</param>
/// <param name="ManagedTotal">Total de contas geridas do tenant.</param>
public sealed record DirectoryAttributePlan(
	IReadOnlyList<DirectoryAttributeUpdate> Updates,
	IReadOnlyList<DirectoryAttributeNotice> Notices,
	int Cleared,
	int ManagedTotal);

/// <summary>
/// Planejamento puro dos atributos de pessoa vindos do diretório (ADR-0055): sem I/O, DI nem log. Decide o que
/// gravar, que avisos levantar e se o freio de limpeza em massa dispara.
/// </summary>
public static class DirectoryAttributePlanner
{
	/// <summary>Calcula o plano a partir das contas vinculadas e dos atributos lidos.</summary>
	/// <param name="linked">Contas vinculadas ao diretório (geridas ou não).</param>
	/// <param name="read">Atributos crus lidos do diretório.</param>
	public static DirectoryAttributePlan Plan(
		IReadOnlyList<DirectoryAttributeAccount> linked, IReadOnlyList<DirectoryAttributes> read)
	{
		var byObjectId = new Dictionary<Guid, DirectoryAttributeAccount>();
		foreach (var account in linked)
		{
			byObjectId.TryAdd(account.ObjectId, account);
		}

		var readByObjectId = new Dictionary<Guid, DirectoryAttributes>();
		foreach (var attributes in read)
		{
			readByObjectId.TryAdd(attributes.ObjectId, attributes);
		}

		var targets = new List<(DirectoryAttributeAccount Account, DirectoryAttributes Read)>();
		foreach (var account in linked)
		{
			if (account.DirectoryManaged && readByObjectId.TryGetValue(account.ObjectId, out var attributes))
			{
				targets.Add((account, attributes));
			}
		}

		var notices = new List<DirectoryAttributeNotice>();
		var cleared = 0;
		var texts = new Dictionary<Guid, (string? Title, string? Department, string? Phone)>();
		var desiredManager = new Dictionary<Guid, Guid?>();

		foreach (var (account, attributes) in targets)
		{
			var title = DirectoryAttributeRules.Normalize(
				attributes.JobTitle, DirectoryAttributeRules.JobTitleMaxLength, out var titleDiscarded);
			var department = DirectoryAttributeRules.Normalize(
				attributes.Department, DirectoryAttributeRules.DepartmentMaxLength, out var departmentDiscarded);
			var phone = DirectoryAttributeRules.Normalize(
				attributes.BusinessPhone, DirectoryAttributeRules.BusinessPhoneMaxLength, out var phoneDiscarded);
			texts[account.UserId] = (title, department, phone);

			if (titleDiscarded || departmentDiscarded || phoneDiscarded)
			{
				notices.Add(new DirectoryAttributeNotice(account.UserId, DirectoryAttributeWarning.FieldDiscarded));
			}

			desiredManager[account.UserId] = ResolveManager(account, attributes, byObjectId, notices);

			// Só vazio cru do diretório conta: nulo por descarte ou por resolução é decisão nossa.
			var clearedText =
				(account.JobTitle is not null && string.IsNullOrWhiteSpace(attributes.JobTitle))
				|| (account.Department is not null && string.IsNullOrWhiteSpace(attributes.Department))
				|| (account.BusinessPhone is not null && string.IsNullOrWhiteSpace(attributes.BusinessPhone));
			var clearedManager = account.ManagerId is not null && attributes.ManagerObjectId is null;
			if (clearedText || clearedManager)
			{
				cleared++;
			}
		}

		var cyclic = FindCycles(linked, desiredManager);
		foreach (var userId in cyclic)
		{
			desiredManager[userId] = null;
			notices.Add(new DirectoryAttributeNotice(userId, DirectoryAttributeWarning.ManagerCycle));
		}

		var updates = new List<DirectoryAttributeUpdate>();
		foreach (var (account, _) in targets)
		{
			var (title, department, phone) = texts[account.UserId];
			var manager = desiredManager[account.UserId];
			var changed = account.JobTitle != title
				|| account.Department != department
				|| account.BusinessPhone != phone
				|| account.ManagerId != manager
				|| account.Origin != DirectoryAttributesOrigin.Directory;
			if (changed)
			{
				updates.Add(new DirectoryAttributeUpdate(account.UserId, title, department, phone, manager));
			}
		}

		return new DirectoryAttributePlan(
			updates,
			notices.OrderBy(n => n.UserId).ThenBy(n => n.Warning).ToList(),
			cleared,
			linked.Count(a => a.DirectoryManaged));
	}

	/// <summary>
	/// O freio dispara quando a limpeza crua passa do piso e da fração das contas geridas — protege contra leitura
	/// ruim do diretório.
	/// </summary>
	/// <param name="plan">Plano calculado.</param>
	/// <param name="options">Opções do ciclo (piso e fração).</param>
	public static bool ExceedsBrake(DirectoryAttributePlan plan, DirectorySyncOptions options) =>
		plan.Cleared > options.EffectiveMinRemovalsBeforeBrake
		&& plan.ManagedTotal > 0
		&& (double)plan.Cleared / plan.ManagedTotal > options.EffectiveMaxRemovalRatio;

	private static Guid? ResolveManager(
		DirectoryAttributeAccount account,
		DirectoryAttributes attributes,
		Dictionary<Guid, DirectoryAttributeAccount> byObjectId,
		List<DirectoryAttributeNotice> notices)
	{
		if (attributes.ManagerObjectId is not { } managerObjectId)
		{
			return null;
		}

		if (!byObjectId.TryGetValue(managerObjectId, out var manager))
		{
			notices.Add(new DirectoryAttributeNotice(account.UserId, DirectoryAttributeWarning.ManagerNotFound));
			return null;
		}

		if (manager.UserId == account.UserId)
		{
			notices.Add(new DirectoryAttributeNotice(account.UserId, DirectoryAttributeWarning.ManagerSelf));
			return null;
		}

		if (!manager.IsActive)
		{
			notices.Add(new DirectoryAttributeNotice(account.UserId, DirectoryAttributeWarning.ManagerInactive));
			return null;
		}

		return manager.UserId;
	}

	/// <summary>
	/// Alvos que estão num ciclo. Arestas: gestor desejado dos alvos e gestor gravado das contas que não são alvo
	/// (essas só participam do caminho, nunca são alteradas). Percurso O(n) com marcação de estado.
	/// </summary>
	private static List<Guid> FindCycles(
		IReadOnlyList<DirectoryAttributeAccount> linked, Dictionary<Guid, Guid?> desiredManager)
	{
		var next = new Dictionary<Guid, Guid?>();
		foreach (var account in linked)
		{
			next[account.UserId] = desiredManager.TryGetValue(account.UserId, out var desired)
				? desired
				: account.ManagerId;
		}

		// 1 = no caminho corrente, 2 = já resolvido.
		var state = new Dictionary<Guid, int>();
		var cyclic = new List<Guid>();

		foreach (var start in desiredManager.Keys)
		{
			var path = new List<Guid>();
			Guid? current = start;
			while (current is { } node && !state.ContainsKey(node))
			{
				state[node] = 1;
				path.Add(node);
				current = next.GetValueOrDefault(node);
			}

			if (current is { } reentry && state[reentry] == 1)
			{
				var from = path.IndexOf(reentry);
				for (var i = from; i < path.Count; i++)
				{
					if (desiredManager.ContainsKey(path[i]))
					{
						cyclic.Add(path[i]);
					}
				}
			}

			foreach (var node in path)
			{
				state[node] = 2;
			}
		}

		return cyclic;
	}
}
