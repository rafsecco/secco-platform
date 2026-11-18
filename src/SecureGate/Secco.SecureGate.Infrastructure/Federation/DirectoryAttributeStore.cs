using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Infrastructure.Contexts;

namespace Secco.SecureGate.Infrastructure.Federation;

/// <summary>
/// Persistência dos atributos de pessoa vindos do diretório (ADR-0055). Roda no escopo do tenant que o chamador
/// entrou (ADR-0041): o filtro do EF e o RLS isolam; os <c>Where</c> explícitos por tenant só tornam a intenção
/// legível e impedem um id de outro tenant de passar.
/// </summary>
internal sealed class DirectoryAttributeStore(SecureGateDbContext context) : IDirectoryAttributeStore
{
	public async Task<IReadOnlyList<DirectoryAttributeAccount>> ListLinkedAsync(
		Guid tenantId, Guid directoryId, CancellationToken cancellationToken = default)
	{
		var prefix = DirectoryLinkKey.Prefix(directoryId);

		// Só contas DONAS do tenant, como na reconciliação de perfis (ADR-0026/0040)
		var links = await (
			from login in context.UserLogins.AsNoTracking()
			join user in context.Users.AsNoTracking() on login.UserId equals user.Id
			where login.LoginProvider == DirectoryLinkKey.Provider
				&& login.ProviderKey.StartsWith(prefix)
				&& user.TenantId == tenantId
			select new
			{
				user.Id,
				login.ProviderKey,
				user.DirectoryManaged,
				user.LockoutEnabled,
				user.LockoutEnd,
				user.JobTitle,
				user.Department,
				user.BusinessPhone,
				user.ManagerId,
				user.AttributesOrigin,
			})
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		var accounts = new Dictionary<Guid, DirectoryAttributeAccount>();
		var now = DateTimeOffset.UtcNow;

		foreach (var link in links)
		{
			// Chave malformada ou de outro formato: ignorada, nunca adivinhada
			if (DirectoryLinkKey.TryGetObjectId(link.ProviderKey, directoryId, out var objectId))
			{
				accounts.TryAdd(
					link.Id,
					new DirectoryAttributeAccount(
						link.Id,
						objectId,
						link.DirectoryManaged,
						UserStatuses.From(link.LockoutEnabled, link.LockoutEnd, now) == UserStatuses.Active,
						link.JobTitle,
						link.Department,
						link.BusinessPhone,
						link.ManagerId,
						link.AttributesOrigin));
			}
		}

		return [.. accounts.Values];
	}

	public async Task<int> ApplyAsync(
		Guid tenantId, IReadOnlyList<DirectoryAttributeUpdate> updates, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(updates);

		if (updates.Count == 0)
		{
			return 0;
		}

		var ids = updates.Select(update => update.UserId).Distinct().ToList();

		// Só contas donas do tenant: um id de outro tenant não é gravado por engano
		var users = await context.Users
			.Where(user => user.TenantId == tenantId && ids.Contains(user.Id))
			.ToDictionaryAsync(user => user.Id, cancellationToken).ConfigureAwait(false);

		var applied = 0;

		foreach (var update in updates)
		{
			if (!users.TryGetValue(update.UserId, out var user))
			{
				continue;
			}

			user.JobTitle = update.JobTitle;
			user.Department = update.Department;
			user.BusinessPhone = update.BusinessPhone;
			user.ManagerId = update.ManagerId;
			user.AttributesOrigin = DirectoryAttributesOrigin.Directory;
			applied++;
		}

		if (applied > 0)
		{
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}

		return applied;
	}
}
