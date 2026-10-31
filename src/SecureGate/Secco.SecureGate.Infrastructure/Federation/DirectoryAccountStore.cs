using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SecureGate.Infrastructure.Users;

namespace Secco.SecureGate.Infrastructure.Federation;

/// <summary>
/// Persistência do provisionamento de contas pelo diretório (ADR-0055, entrega B) sobre o ASP.NET Identity. Roda no
/// escopo do tenant que o chamador entrou (ADR-0041); só a unicidade do e-mail e a lista de tenants do primeiro
/// login atravessam tenants, e cada uma entra em modo plataforma listado na allowlist.
/// </summary>
internal sealed class DirectoryAccountStore(
	SecureGateDbContext context,
	UserManager<User> userManager,
	UserAccountService users,
	ITenantAccessScope accessScope) : IDirectoryAccountStore
{
	public Task<int> CountManagedAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		context.Users.AsNoTracking()
			.CountAsync(user => user.TenantId == tenantId && user.DirectoryManaged, cancellationToken);

	public async Task<bool> EmailInUseAsync(string email, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(email);

		// O e-mail é único na instalação (ADR-0022) e a conta que o tem pode ser de OUTRO tenant, invisível ao
		// escopo corrente: sem o modo plataforma a colisão passaria despercebida até o índice único estourar
		using (accessScope.EnterPlatform("provisionamento pelo diretório: o e-mail é único na instalação e a colisão pode ser com conta de outro tenant"))
		{
			var normalizedEmail = userManager.NormalizeEmail(email);
			var normalizedName = userManager.NormalizeName(email);

			return await context.Users.AsNoTracking()
				.AnyAsync(
					user => user.NormalizedEmail == normalizedEmail || user.NormalizedUserName == normalizedName,
					cancellationToken)
				.ConfigureAwait(false);
		}
	}

	public async Task<CreateDirectoryAccountOutcome> CreateAsync(
		Guid tenantId,
		Guid directoryId,
		Guid userId,
		Guid objectId,
		DirectoryAccountDraft draft,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(draft);

		if (await EmailInUseAsync(draft.Email, cancellationToken).ConfigureAwait(false))
		{
			return CreateDirectoryAccountOutcome.EmailInUse;
		}

		// Conta, login externo e vínculo de dono: tudo ou nada. A estratégia de execução envolve a transação para o
		// caso de retry configurado no provider
		var strategy = context.Database.CreateExecutionStrategy();

		return await strategy.ExecuteAsync(async () =>
		{
			await using var transaction = await context.Database
				.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

			var user = new User
			{
				Id = userId,
				TenantId = tenantId,
				UserName = draft.Email,
				Email = draft.Email,
				// O diretório do cliente prova a identidade no login; o e-mail vem do Graph, e a conta nasce sem
				// senha e sem convite — a credencial é só o login federado (ADR-0026)
				EmailConfirmed = true,
				LocalLoginEnabled = false,
				DisplayName = draft.DisplayName,
				DirectoryManaged = true,
			};

			try
			{
				var created = await userManager.CreateAsync(user).ConfigureAwait(false);

				if (!created.Succeeded)
				{
					if (created.Errors.Any(error => error.Code is "DuplicateUserName" or "DuplicateEmail"))
					{
						return CreateDirectoryAccountOutcome.EmailInUse;
					}

					// Falha de infraestrutura, não regra de negócio. Só os códigos, nunca a descrição (ADR-0020)
					throw new InvalidOperationException(
						"O Identity recusou a criação da conta do diretório: " + string.Join(", ", created.Errors.Select(e => e.Code)));
				}

				context.UserLogins.Add(new UserLogin
				{
					UserId = userId,
					LoginProvider = DirectoryLinkKey.Provider,
					ProviderKey = DirectoryLinkKey.Build(directoryId, objectId),
					ProviderDisplayName = "Microsoft Entra ID",
				});
				context.TenantMemberships.Add(new TenantMembership(userId, tenantId, DateTimeOffset.UtcNow));

				await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

				return CreateDirectoryAccountOutcome.Created;
			}
			catch (DbUpdateException)
			{
				// Outro pedido criou a conta com o mesmo e-mail (ou vínculo) entre a checagem e o INSERT: o índice
				// único manda. Se o e-mail agora existe, é colisão; qualquer outra falha sobe.
				context.ChangeTracker.Clear();

				if (await EmailInUseAsync(draft.Email, cancellationToken).ConfigureAwait(false))
				{
					return CreateDirectoryAccountOutcome.EmailInUse;
				}

				throw;
			}
		}).ConfigureAwait(false);
	}

	public async Task<bool> AdoptAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

		// Conta de outro tenant responde como inexistente: a sincronização é por tenant
		if (user is null || user.TenantId != tenantId)
		{
			return false;
		}

		if (user.DirectoryManaged)
		{
			return true;
		}

		user.DirectoryManaged = true;
		var updated = await userManager.UpdateAsync(user).ConfigureAwait(false);

		if (!updated.Succeeded)
		{
			throw new InvalidOperationException(
				"O Identity recusou a adoção da conta: " + string.Join(", ", updated.Errors.Select(e => e.Code)));
		}

		return true;
	}

	// A MESMA operação da desativação manual (lockout sem fim), só com outra origem gravada
	public async Task<bool> DeactivateAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

		// Re-confere no momento da mudança: o plano foi calculado antes, e uma desativação manual nesse meio-tempo
		// não pode ter a origem sobrescrita para Directory — o ciclo seguinte a desfaria
		if (user is null
			|| user.TenantId != tenantId
			|| !user.DirectoryManaged
			|| UserStatuses.From(user.LockoutEnabled, user.LockoutEnd, DateTimeOffset.UtcNow) == UserStatuses.Deactivated)
		{
			return false;
		}

		return await users.SetActiveAsync(tenantId, userId, active: false, DeactivationOrigin.Directory).ConfigureAwait(false);
	}

	public async Task<bool> ReactivateAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

		// Só desfaz o que a própria sincronização fez: desativação manual (ou conta de outro tenant) nunca volta
		if (user is null || user.TenantId != tenantId || user.DeactivationOrigin != DeactivationOrigin.Directory)
		{
			return false;
		}

		return await users.SetActiveAsync(tenantId, userId, active: true, DeactivationOrigin.None).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<ProvisioningTarget>> ListProvisioningTargetsAsync(
		Guid directoryId, CancellationToken cancellationToken = default)
	{
		// Atravessa tenants por natureza: no primeiro login ainda não existe tenant, só o tid do diretório
		using (accessScope.EnterPlatform("login federado: acha os tenants cujo grupo de provisionamento pode criar a conta, antes de existir tenant"))
		{
			var rows = await (
				from federation in context.TenantFederations.AsNoTracking()
				join tenant in context.Tenants.AsNoTracking() on federation.TenantId equals tenant.Id
				join mapping in context.TenantGroupRoleMappings.AsNoTracking() on tenant.Id equals mapping.TenantId
				join role in context.Roles.AsNoTracking() on mapping.RoleId equals role.Id
				where federation.IsEnabled
					&& federation.DirectoryId == directoryId
					&& tenant.IsActive
					&& tenant.Id != SecureGatePlatform.TenantId
					&& mapping.IsProvisioningGroup
				orderby tenant.CreatedAt, tenant.Id
				select new { tenant.Id, mapping.EntraGroupId, role.Name, federation.AllowedEmailDomains })
				.ToListAsync(cancellationToken).ConfigureAwait(false);

			return
			[
				.. rows.Select(row => new ProvisioningTarget(
					row.Id, row.EntraGroupId, row.Name!, EmailDomainRules.Deserialize(row.AllowedEmailDomains)))
			];
		}
	}
}
