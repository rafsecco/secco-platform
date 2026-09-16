using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Monta tenants, perfis e usuários direto no banco — o que se testa é a API de gestão, não a
/// criação por ela.
/// </summary>
internal static class IdentitySeed
{
	public const string Password = "Perf1l@Secco!";

	/// <summary>Client com token HS256 de testes e <c>securegate:admin</c> — sub não é usuário (máquina).</summary>
	public static HttpClient AdminClient(SecureGateApiFactory factory)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(SecureGateScopes.Admin));

		return client;
	}

	public static async Task<Guid> TenantAsync(SecureGateApiFactory factory)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		var tenant = new Tenant("Tenant de perfis", $"t-{Guid.NewGuid():N}");
		context.Tenants.Add(tenant);
		await context.SaveChangesAsync();

		return tenant.Id;
	}

	public static async Task RoleAsync(SecureGateApiFactory factory, Guid tenantId, string name, params string[] permissions)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		var role = new Role
		{
			Id = Guid.CreateVersion7(),
			TenantId = tenantId,
			Name = name,
			NormalizedName = name.ToUpperInvariant(),
			ConcurrencyStamp = Guid.NewGuid().ToString(),
		};
		context.Roles.Add(role);
		context.RoleClaims.AddRange(permissions.Select(permission =>
			new RoleClaim { RoleId = role.Id, ClaimType = "permission", ClaimValue = permission }));
		await context.SaveChangesAsync();
	}

	public static async Task<Guid> UserAsync(SecureGateApiFactory factory, Guid tenantId, string email, params string[] roles)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		var user = new User
		{
			Id = Guid.CreateVersion7(),
			TenantId = tenantId,
			UserName = email,
			Email = email,
			EmailConfirmed = true,
		};
		(await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();

		foreach (var roleName in roles)
		{
			var normalized = roleName.ToUpperInvariant();
			var role = await context.Roles.FirstAsync(r => r.TenantId == tenantId && r.NormalizedName == normalized);
			context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
		}

		await context.SaveChangesAsync();

		return user.Id;
	}

	public static Task<Guid> PlatformOperatorAsync(SecureGateApiFactory factory, string email) =>
		UserAsync(factory, SecureGatePlatform.TenantId, email, SecureGatePlatform.OperatorRole);

	public static Task DeactivateAsync(SecureGateApiFactory factory, Guid userId) =>
		SetLockoutAsync(factory, userId, DateTimeOffset.MaxValue);

	public static Task LockOutAsync(SecureGateApiFactory factory, Guid userId, DateTimeOffset until) =>
		SetLockoutAsync(factory, userId, until);

	private static async Task SetLockoutAsync(SecureGateApiFactory factory, Guid userId, DateTimeOffset until)
	{
		using var scope = factory.Services.CreateScope();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		var user = await userManager.FindByIdAsync(userId.ToString());
		(await userManager.SetLockoutEnabledAsync(user!, true)).Succeeded.Should().BeTrue();
		(await userManager.SetLockoutEndDateAsync(user!, until)).Succeeded.Should().BeTrue();
	}
}
