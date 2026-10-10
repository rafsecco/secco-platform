using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Secco.SecureGate.Application.Permissions;
using Secco.SecureGate.Domain.Permissions;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SharedKernel.Authorization;

namespace Secco.SecureGate.Infrastructure.PermissionCatalog;

/// <summary>
/// Persistência EF Core do catálogo de permissões (ADR-0039) sobre o banco de plataforma. As
/// comparações de nome e de produto são refeitas em memória de forma ORDINAL: o SQL Server compara
/// sem diferenciar caixa por padrão, e o catálogo é case-sensitive.
/// </summary>
internal sealed partial class PermissionCatalogRepository(
	SecureGateDbContext context, ILogger<PermissionCatalogRepository> logger) : IPermissionCatalogRepository
{
	public async Task<IReadOnlyList<string>> ReplaceAsync(
		string product,
		IReadOnlyList<SeccoPermissionDefinition> permissions,
		DateTimeOffset now,
		CancellationToken cancellationToken = default)
	{
		var current = (await context.PermissionCatalogEntries
				.Where(e => e.Product == product)
				.ToListAsync(cancellationToken).ConfigureAwait(false))
			.Where(e => string.Equals(e.Product, product, StringComparison.Ordinal))
			.ToList();

		var declared = permissions.ToDictionary(p => p.Name, StringComparer.Ordinal);
		var existing = current.ToDictionary(e => e.Name, StringComparer.Ordinal);

		context.PermissionCatalogEntries.RemoveRange(current.Where(e => !declared.ContainsKey(e.Name)));

		foreach (var permission in permissions)
		{
			if (!existing.TryGetValue(permission.Name, out var entry))
			{
				context.PermissionCatalogEntries.Add(
					new PermissionCatalogEntry(product, permission.Name, permission.Description, now));
			}
			else if (!string.Equals(entry.Description, permission.Description, StringComparison.Ordinal))
			{
				entry.UpdateDescription(permission.Description, now);
			}
		}

		// Uma única SaveChanges é uma única transação: o conjunto do produto troca por inteiro ou não troca
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		var collisions = await FindCollisionsAsync(product, declared.Keys, cancellationToken).ConfigureAwait(false);

		// Produto e nome já validados (formato restrito) — nunca a descrição (ADR-0020)
		foreach (var name in collisions)
		{
			LogNameCollision(logger, product, name);
		}

		return collisions;
	}

	public async Task<IReadOnlyList<ProductPermissionCatalogDto>> ListAsync(CancellationToken cancellationToken = default)
	{
		var entries = await context.PermissionCatalogEntries
			.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

		return BuildCatalogs(entries);
	}

	public async Task<ProductPermissionCatalogDto?> GetAsync(string product, CancellationToken cancellationToken = default)
	{
		var own = (await context.PermissionCatalogEntries.AsNoTracking()
				.Where(e => e.Product == product)
				.ToListAsync(cancellationToken).ConfigureAwait(false))
			.Where(e => string.Equals(e.Product, product, StringComparison.Ordinal))
			.ToList();

		if (own.Count == 0)
		{
			return null;
		}

		var names = own.Select(e => e.Name).ToList();
		var others = await context.PermissionCatalogEntries.AsNoTracking()
			.Where(e => e.Product != product && names.Contains(e.Name))
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		return BuildCatalogs([.. own, .. others]).Single(c => string.Equals(c.Product, product, StringComparison.Ordinal));
	}

	public async Task<IReadOnlyList<string>> FindUnknownAsync(IEnumerable<string> names, CancellationToken cancellationToken = default)
	{
		var requested = names.Distinct(StringComparer.Ordinal).ToList();

		if (requested.Count == 0)
		{
			return [];
		}

		var found = (await context.PermissionCatalogEntries.AsNoTracking()
				.Where(e => requested.Contains(e.Name))
				.Select(e => e.Name)
				.Distinct()
				.ToListAsync(cancellationToken).ConfigureAwait(false))
			.ToHashSet(StringComparer.Ordinal);

		return [.. requested.Where(name => !found.Contains(name))];
	}

	private async Task<IReadOnlyList<string>> FindCollisionsAsync(
		string product, IEnumerable<string> names, CancellationToken cancellationToken)
	{
		var declared = names.ToHashSet(StringComparer.Ordinal);

		if (declared.Count == 0)
		{
			return [];
		}

		var declaredList = declared.ToList();
		var others = await context.PermissionCatalogEntries.AsNoTracking()
			.Where(e => e.Product != product && declaredList.Contains(e.Name))
			.Select(e => e.Name)
			.Distinct()
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		return [.. others.Where(declared.Contains).Order(StringComparer.Ordinal)];
	}

	[LoggerMessage(
		EventId = 1,
		Level = LogLevel.Warning,
		Message = "Catálogo de permissões: o produto {Product} declara '{Permission}', nome que outro produto também declara (colisão visível, ADR-0039).")]
	private static partial void LogNameCollision(ILogger logger, string product, string permission);

	private static List<ProductPermissionCatalogDto> BuildCatalogs(IReadOnlyCollection<PermissionCatalogEntry> entries)
	{
		var productsByName = entries
			.GroupBy(e => e.Name, StringComparer.Ordinal)
			.ToDictionary(g => g.Key, g => g.Select(e => e.Product).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

		return [.. entries
			.GroupBy(e => e.Product, StringComparer.Ordinal)
			.OrderBy(g => g.Key, StringComparer.Ordinal)
			.Select(group => new ProductPermissionCatalogDto(
				group.Key,
				[.. group
					.OrderBy(e => e.Name, StringComparer.Ordinal)
					.Select(e => new PermissionDefinitionDto(
						e.Name,
						e.Description,
						[.. productsByName[e.Name]
							.Where(other => !string.Equals(other, group.Key, StringComparison.Ordinal))
							.Order(StringComparer.Ordinal)]))],
				group.Max(e => e.PublishedAt)))];
	}
}
