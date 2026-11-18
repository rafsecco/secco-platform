using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Persistência dos atributos de pessoa vindos do diretório (ADR-0055): a listagem das contas donas vinculadas e a
/// gravação restrita ao tenant, contra o banco real (credencial de runtime, com o RLS valendo).
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class DirectoryAttributeStoreTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"attr-{Guid.NewGuid():N}@secco.test";

	private async Task<(Guid UserId, Guid ObjectId)> LinkAsync(Guid tenantId, Guid directoryId, bool deactivated = false)
	{
		var userId = await IdentitySeed.UserAsync(factory, tenantId, Email());
		var objectId = Guid.NewGuid();

		using var scope = factory.Services.CreatePlatformScope();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
		var user = (await userManager.FindByIdAsync(userId.ToString()))!;
		(await userManager.AddLoginAsync(user, new UserLoginInfo(
			DirectoryLinkKey.Provider, DirectoryLinkKey.Build(directoryId, objectId), "Entra"))).Succeeded.Should().BeTrue();

		if (deactivated)
		{
			(await userManager.SetLockoutEnabledAsync(user, true)).Succeeded.Should().BeTrue();
			(await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue)).Succeeded.Should().BeTrue();
		}

		return (userId, objectId);
	}

	private async Task<T> InTenantAsync<T>(Guid tenantId, Func<IDirectoryAttributeStore, Task<T>> action)
	{
		using var scope = factory.Services.CreateScope();
		using var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantAccessScope>().EnterTenant(tenantId);

		return await action(scope.ServiceProvider.GetRequiredService<IDirectoryAttributeStore>());
	}

	private async Task<User> UserAsync(Guid userId)
	{
		using var scope = factory.Services.CreatePlatformScope();

		return await scope.ServiceProvider.GetRequiredService<SecureGateDbContext>().Users.AsNoTracking()
			.SingleAsync(u => u.Id == userId);
	}

	[Fact]
	public async Task ListLinked_SoDonasVinculadasAoDiretorio()
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		var directoryId = Guid.NewGuid();
		var linked = await LinkAsync(tenantId, directoryId);
		await LinkAsync(tenantId, Guid.NewGuid()); // vinculada a outro diretório
		await IdentitySeed.UserAsync(factory, tenantId, Email()); // só local, sem login externo

		// Membro por vínculo (ADR-0040): conta dona de outro tenant, vinculada ao mesmo diretório
		var otherTenantId = await IdentitySeed.TenantAsync(factory);
		var member = await LinkAsync(otherTenantId, directoryId);
		using (var scope = factory.Services.CreatePlatformScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			context.TenantMemberships.Add(new TenantMembership(member.UserId, tenantId, DateTimeOffset.UtcNow));
			await context.SaveChangesAsync();
		}

		var accounts = await InTenantAsync(tenantId, store => store.ListLinkedAsync(tenantId, directoryId));

		accounts.Should().ContainSingle().Which.Should().BeEquivalentTo(new
		{
			linked.UserId,
			linked.ObjectId,
			DirectoryManaged = false,
			IsActive = true,
			JobTitle = (string?)null,
			ManagerId = (Guid?)null,
			Origin = DirectoryAttributesOrigin.None,
		});
	}

	[Fact]
	public async Task ListLinked_ContaDesativada_IsActiveFalse()
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		var directoryId = Guid.NewGuid();
		await LinkAsync(tenantId, directoryId, deactivated: true);

		var accounts = await InTenantAsync(tenantId, store => store.ListLinkedAsync(tenantId, directoryId));

		accounts.Should().ContainSingle().Which.IsActive.Should().BeFalse();
	}

	[Fact]
	public async Task Apply_GravaCamposEMarcaOrigem_ENaoTocaNoNome()
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		var directoryId = Guid.NewGuid();
		var boss = await LinkAsync(tenantId, directoryId);
		var person = await LinkAsync(tenantId, directoryId);

		using (var scope = factory.Services.CreatePlatformScope())
		{
			var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
			var user = (await userManager.FindByIdAsync(person.UserId.ToString()))!;
			user.DisplayName = "Nome Escolhido";
			(await userManager.UpdateAsync(user)).Succeeded.Should().BeTrue();
		}

		var applied = await InTenantAsync(tenantId, store => store.ApplyAsync(
			tenantId, [new DirectoryAttributeUpdate(person.UserId, "Analista", "TI", "+55 11 5555-0000", boss.UserId)]));

		applied.Should().Be(1);
		(await UserAsync(person.UserId)).Should().BeEquivalentTo(new
		{
			JobTitle = "Analista",
			Department = "TI",
			BusinessPhone = "+55 11 5555-0000",
			ManagerId = (Guid?)boss.UserId,
			AttributesOrigin = DirectoryAttributesOrigin.Directory,
			DisplayName = "Nome Escolhido",
		});
	}

	[Fact]
	public async Task Apply_ContaDeOutroTenant_NaoGrava()
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		var otherTenantId = await IdentitySeed.TenantAsync(factory);
		var foreign = await LinkAsync(otherTenantId, Guid.NewGuid());

		var applied = await InTenantAsync(tenantId, store => store.ApplyAsync(
			tenantId, [new DirectoryAttributeUpdate(foreign.UserId, "Analista", null, null, null)]));

		applied.Should().Be(0);
		(await UserAsync(foreign.UserId)).Should().BeEquivalentTo(new
		{
			JobTitle = (string?)null,
			AttributesOrigin = DirectoryAttributesOrigin.None,
		});
	}
}
