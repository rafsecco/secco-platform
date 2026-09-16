using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Factory com banco PRÓPRIO: contar "operadores ativos" só é determinístico sem as outras classes
/// criando operadores no mesmo tenant de plataforma.
/// </summary>
public sealed class OperatorGuardSecureGateApiFactory : SecureGateApiFactory;

/// <summary>Collection isolada da guarda de último operador.</summary>
[CollectionDefinition(Name)]
public sealed class OperatorGuardApiCollectionDefinition : ICollectionFixture<OperatorGuardSecureGateApiFactory>
{
	/// <summary>Nome da collection.</summary>
	public const string Name = "SecureGate guarda de último operador";
}

/// <summary>
/// A instalação nunca fica sem operador ativo. Executado por client de MÁQUINA com
/// <c>securegate:admin</c> (o sub não é usuário): é o caso que a guarda de "não desativar a si
/// mesmo" não cobre.
/// </summary>
[Collection(OperatorGuardApiCollectionDefinition.Name)]
public class LastOperatorGuardTests(OperatorGuardSecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly string OperatorRoleUrl = SecureGatePlatform.OperatorRole;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();

		// Começa sem nenhum operador: cada teste monta exatamente o cenário que prova
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var normalized = SecureGatePlatform.OperatorRole.ToUpperInvariant();
		var roleId = await context.Roles
			.Where(r => r.TenantId == SecureGatePlatform.TenantId && r.NormalizedName == normalized)
			.Select(r => r.Id)
			.SingleAsync();
		context.UserRoles.RemoveRange(await context.UserRoles.Where(ur => ur.RoleId == roleId).ToListAsync());
		await context.SaveChangesAsync();
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"op-guarda-{Guid.NewGuid():N}@secco.test";

	private Task<HttpResponseMessage> RemoveOperatorAsync(Guid userId) =>
		IdentitySeed.AdminClient(factory)
			.DeleteAsync($"/api/v1/tenants/{SecureGatePlatform.TenantId}/users/{userId}/roles/{OperatorRoleUrl}");

	private Task<HttpResponseMessage> DeactivateAsync(Guid userId) =>
		IdentitySeed.AdminClient(factory)
			.PostAsync($"/api/v1/tenants/{SecureGatePlatform.TenantId}/users/{userId}/deactivate", null);

	[Fact]
	public async Task Remover_UltimoOperadorAtivo_Retorna409()
	{
		var only = await IdentitySeed.PlatformOperatorAsync(factory, Email());

		(await RemoveOperatorAsync(only)).StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task Desativar_UltimoOperadorAtivo_Retorna409()
	{
		var only = await IdentitySeed.PlatformOperatorAsync(factory, Email());

		(await DeactivateAsync(only)).StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task Remover_ComOutroOperadorAtivo_Retorna204()
	{
		var first = await IdentitySeed.PlatformOperatorAsync(factory, Email());
		await IdentitySeed.PlatformOperatorAsync(factory, Email());

		(await RemoveOperatorAsync(first)).StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	[Fact]
	public async Task Remover_QuandoOOutroEstaDesativado_Retorna409()
	{
		var active = await IdentitySeed.PlatformOperatorAsync(factory, Email());
		var inactive = await IdentitySeed.PlatformOperatorAsync(factory, Email());
		await IdentitySeed.DeactivateAsync(factory, inactive);

		// Operador desativado não opera: não conta como "outro ativo"
		(await RemoveOperatorAsync(active)).StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task Remover_OperadorDesativado_ComUmAtivo_Retorna204()
	{
		await IdentitySeed.PlatformOperatorAsync(factory, Email());
		var inactive = await IdentitySeed.PlatformOperatorAsync(factory, Email());
		await IdentitySeed.DeactivateAsync(factory, inactive);

		(await RemoveOperatorAsync(inactive)).StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	[Fact]
	public async Task Desativar_ComOutroOperadorAtivo_Retorna204()
	{
		var first = await IdentitySeed.PlatformOperatorAsync(factory, Email());
		await IdentitySeed.PlatformOperatorAsync(factory, Email());

		(await DeactivateAsync(first)).StatusCode.Should().Be(HttpStatusCode.NoContent);
	}
}
