using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Infrastructure.Contexts;

namespace Secco.SecureGate.Infrastructure.Federation;

/// <summary>
/// Quais tenants o ciclo de reconciliação percorre (ADR-0055). A listagem atravessa tenants por natureza e roda
/// fora de requisição, então entra em modo plataforma — a única exceção, listada na allowlist (ADR-0041); cada
/// tenant é depois reconciliado no escopo dele.
/// </summary>
internal sealed class DirectorySyncTenantSource(SecureGateDbContext context, ITenantAccessScope accessScope)
	: IDirectorySyncTenantSource
{
	public async Task<IReadOnlyList<DirectorySyncTenant>> ListEligibleAsync(CancellationToken cancellationToken = default)
	{
		using (accessScope.EnterPlatform("ciclo de sincronização do diretório: lista os tenants com federação ativa e o que reconciliar, fora de requisição"))
		{
			return await (
				from federation in context.TenantFederations.AsNoTracking()
				join tenant in context.Tenants.AsNoTracking() on federation.TenantId equals tenant.Id
				where federation.IsEnabled
					&& tenant.IsActive
					&& tenant.Id != SecureGatePlatform.TenantId
					&& (context.TenantGroupRoleMappings.Any(mapping => mapping.TenantId == tenant.Id)
						// Mapeamento removido não pode deixar perfil preso: a atribuição de origem Directory
						// sem mapeamento também põe o tenant no ciclo (sem Graph; é só desfazer)
						|| (from userRole in context.UserRoles
							join role in context.Roles on userRole.RoleId equals role.Id
							where role.TenantId == tenant.Id && userRole.Origin == RoleAssignmentOrigin.Directory
							select userRole.UserId).Any())
				select new DirectorySyncTenant(tenant.Id, federation.DirectoryId))
				.ToListAsync(cancellationToken).ConfigureAwait(false);
		}
	}
}
