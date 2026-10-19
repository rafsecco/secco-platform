using FluentAssertions;
using NSubstitute;
using Secco.AdminPortal.Services;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>
/// Concessões de admin de tenant no portal (ADR-0044): o serviço projeta os DTOs do client, monta
/// "tenants que a pessoa administra" a partir dos admins de cada tenant e chama as rotas de concessão
/// com o client do operador (ADR-0023).
/// </summary>
public class TenantAdminGrantServiceTests
{
	private static readonly Guid TenantA = Guid.NewGuid();
	private static readonly Guid TenantB = Guid.NewGuid();
	private static readonly Guid Person = Guid.NewGuid();

	private static (ISecureGateClientFactory Factory, ISecureGateClient Client) BuildFactory()
	{
		var client = Substitute.For<ISecureGateClient>();
		var factory = Substitute.For<ISecureGateClientFactory>();
		factory.CreateAsync(Arg.Any<CancellationToken>()).Returns(client);

		return (factory, client);
	}

	[Fact]
	public async Task ListarAdmins_ProjetaPessoaTenantDonoEConcessao()
	{
		var (factory, client) = BuildFactory();
		var grantedAt = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
		client.ListTenantAdminsAsync(TenantB, Arg.Any<CancellationToken>()).Returns(new List<TenantAdminDto>
		{
			new()
			{
				UserId = Person, Email = "ana@empresa.test", DisplayName = "Ana",
				OwnerTenantId = TenantA, GrantedBy = "operador", CreatedAt = grantedAt,
			},
		});

		var admins = await new SecureGateTenantAdminService(factory).ListAdminsAsync(TenantB);

		admins.Should().ContainSingle().Which.Should().Be(
			new TenantAdminSummary(Person, "ana@empresa.test", "Ana", TenantA, "operador", grantedAt));
	}

	[Fact]
	public async Task TenantsAdministrados_SaoOsQueListamAPessoa()
	{
		var (factory, client) = BuildFactory();
		client.ListTenantsAsync(Arg.Any<CancellationToken>()).Returns(new List<TenantDto>
		{
			new() { Id = TenantA, Name = "Intranet", Slug = "intranet", IsActive = true },
			new() { Id = TenantB, Name = "Compras", Slug = "compras", IsActive = true },
		});
		client.ListTenantAdminsAsync(TenantA, Arg.Any<CancellationToken>()).Returns(new List<TenantAdminDto>());
		client.ListTenantAdminsAsync(TenantB, Arg.Any<CancellationToken>()).Returns(new List<TenantAdminDto>
		{
			new() { UserId = Person, Email = "ana@empresa.test", OwnerTenantId = TenantA, GrantedBy = "operador" },
		});

		var administered = await new SecureGateTenantAdminService(factory).ListAdministeredByAsync(Person);

		administered.Select(tenant => tenant.Id).Should().Equal(TenantB);
	}

	[Fact]
	public async Task ConcederERevogar_ChamamAsRotasDoTenantEDaPessoa()
	{
		var (factory, client) = BuildFactory();
		var service = new SecureGateTenantAdminService(factory);

		await service.GrantAdminAsync(TenantB, Person);
		await service.RevokeAdminAsync(TenantB, Person);

		await client.Received(1).GrantTenantAdminAsync(TenantB, Person, Arg.Any<CancellationToken>());
		await client.Received(1).RevokeTenantAdminAsync(TenantB, Person, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Conceder_ErroDoClientPropaga()
	{
		var (factory, client) = BuildFactory();
		client.GrantTenantAdminAsync(TenantB, Person, Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new ApiException("conflito", 409, "{}", new Dictionary<string, IEnumerable<string>>(), null)));

		var act = () => new SecureGateTenantAdminService(factory).GrantAdminAsync(TenantB, Person);

		(await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(409);
	}
}
