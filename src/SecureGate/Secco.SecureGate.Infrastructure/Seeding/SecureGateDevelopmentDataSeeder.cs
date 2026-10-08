using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;

namespace Secco.SecureGate.Infrastructure.Seeding;

/// <summary>
/// Seed de DESENVOLVIMENTO (ADR-0019, guarda dupla): tenant demo (o mesmo Guid dos
/// appsettings de DEV dos produtos), papel demo e usuários demo com senha conhecida; os clients
/// de DEV vêm de <c>SecureGate:PlatformClients</c> no <c>appsettings.Development.json</c> (ADR-0037) —
/// jamais chega a produção (a orquestração do SDK garante).
/// </summary>
public sealed class SecureGateDevelopmentDataSeeder(
	SecureGateDbContext context,
	UserManager<Identity.User> userManager) : IDevelopmentDataSeeder
{
	/// <summary>Usuário demo do tenant de desenvolvimento.</summary>
	public const string DevUserEmail = "dev@secco.local";

	/// <summary>Senha do usuário demo (conhecida — só existe em DEV; satisfaz a política do Identity).</summary>
	public const string DevUserPassword = "Dev@Secco2026";

	/// <summary>Usuário OPERADOR de instalação (ADR-0023) — recebe o scope admin no login.</summary>
	public const string OperatorEmail = "operador@secco.local";

	/// <summary>Senha do operador demo (conhecida — só existe em DEV).</summary>
	public const string OperatorPassword = "Op3rador@Secco!";
	/// <summary>Tenant demo — mesmo Guid usado nos appsettings.Development dos produtos.</summary>
	public static readonly Guid DemoTenantId = Guid.Parse("018f0000-0000-7000-8000-000000000001");

	/// <summary>Role demo com as permissões do LogStream (Fase 6.4) — referenciado pelo client de console de DEV (em <c>PlatformClients</c>).</summary>
	public const string DevRoleName = "dev-admin";

	/// <summary>Permissões do role demo (strings literais: as constantes vivem em cada produto, ADR-0003).</summary>
	private static readonly string[] DevRolePermissions =
	[
		"log-entries:read", "log-entries:write",
		"log-processes:read", "log-processes:write",
		"api-call-logs:read", "api-call-logs:write",
		"audit-entries:read", "audit-entries:write",
	];

	public async Task SeedAsync(CancellationToken cancellationToken = default)
	{
		if (!await context.Tenants.AnyAsync(t => t.Id == DemoTenantId, cancellationToken).ConfigureAwait(false))
		{
			var tenant = new Tenant("Tenant de Desenvolvimento", "dev-alfa");
			context.Entry(tenant).Property(nameof(Tenant.Id)).CurrentValue = DemoTenantId;
			context.Tenants.Add(tenant);
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}

		await SeedDevRoleAsync(cancellationToken).ConfigureAwait(false);
		await SeedDevUserAsync(cancellationToken).ConfigureAwait(false);

		// Fase 7.1 (ADR-0023): usuário operador de instalação
		await SeedOperatorUserAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Usuário operador de instalação no tenant de plataforma, com o role de operador.</summary>
	private async Task SeedOperatorUserAsync(CancellationToken cancellationToken)
	{
		if (await userManager.FindByNameAsync(OperatorEmail).ConfigureAwait(false) is not null)
		{
			return;
		}

		var user = new Identity.User
		{
			Id = Guid.CreateVersion7(),
			TenantId = Application.SecureGatePlatform.TenantId,
			UserName = OperatorEmail,
			Email = OperatorEmail,
			EmailConfirmed = true,
		};

		if (!(await userManager.CreateAsync(user, OperatorPassword).ConfigureAwait(false)).Succeeded)
		{
			return;
		}

		var normalizedOperator = Application.SecureGatePlatform.OperatorRole.ToUpperInvariant();

		var role = await context.Roles
			.FirstOrDefaultAsync(
				r => r.TenantId == Application.SecureGatePlatform.TenantId && r.NormalizedName == normalizedOperator,
				cancellationToken)
			.ConfigureAwait(false);

		if (role is not null)
		{
			context.UserRoles.Add(new Identity.UserRole { UserId = user.Id, RoleId = role.Id });
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>Usuário demo no tenant de desenvolvimento, com o role demo (para exercitar o login).</summary>
	private async Task SeedDevUserAsync(CancellationToken cancellationToken)
	{
		if (await userManager.FindByNameAsync(DevUserEmail).ConfigureAwait(false) is not null)
		{
			return;
		}

		var user = new Identity.User
		{
			Id = Guid.CreateVersion7(),
			TenantId = DemoTenantId,
			UserName = DevUserEmail,
			Email = DevUserEmail,
			EmailConfirmed = true,
		};

		var created = await userManager.CreateAsync(user, DevUserPassword).ConfigureAwait(false);

		if (!created.Succeeded)
		{
			return;
		}

		var normalizedRole = DevRoleName.ToUpperInvariant();

		var role = await context.Roles
			.FirstOrDefaultAsync(r => r.TenantId == DemoTenantId && r.NormalizedName == normalizedRole, cancellationToken)
			.ConfigureAwait(false);

		if (role is not null)
		{
			context.UserRoles.Add(new Identity.UserRole { UserId = user.Id, RoleId = role.Id });
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>Role demo do tenant de desenvolvimento com as permissões do LogStream.</summary>
	private async Task SeedDevRoleAsync(CancellationToken cancellationToken)
	{
		var normalized = DevRoleName.ToUpperInvariant();

		var role = await context.Roles
			.FirstOrDefaultAsync(r => r.TenantId == DemoTenantId && r.NormalizedName == normalized, cancellationToken)
			.ConfigureAwait(false);

		if (role is not null)
		{
			return;
		}

		role = new Identity.Role
		{
			Id = Guid.CreateVersion7(),
			TenantId = DemoTenantId,
			Name = DevRoleName,
			NormalizedName = normalized,
			ConcurrencyStamp = Guid.NewGuid().ToString(),
		};

		context.Roles.Add(role);
		context.RoleClaims.AddRange(DevRolePermissions.Select(permission => new Identity.RoleClaim
		{
			RoleId = role.Id,
			ClaimType = Roles.RoleRepository.PermissionClaimType,
			ClaimValue = permission,
		}));

		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}
}
