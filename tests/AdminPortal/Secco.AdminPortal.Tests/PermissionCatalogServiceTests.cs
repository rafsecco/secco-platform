using FluentAssertions;
using NSubstitute;
using Secco.AdminPortal.Services;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>
/// Catálogo de permissões no portal (ADR-0039): serviços projetam os DTOs do client e usam o
/// client criado pela fábrica (token do operador, ADR-0023).
/// </summary>
public class PermissionCatalogServiceTests
{
	private static (ISecureGateClientFactory Factory, ISecureGateClient Client) BuildFactory()
	{
		var client = Substitute.For<ISecureGateClient>();
		var factory = Substitute.For<ISecureGateClientFactory>();
		factory.CreateAsync(Arg.Any<CancellationToken>()).Returns(client);

		return (factory, client);
	}

	[Fact]
	public async Task ListarCatalogo_PreservaProdutoDescricaoColisaoEData()
	{
		var (factory, client) = BuildFactory();
		var publishedAt = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
		client.ListPermissionCatalogAsync(Arg.Any<CancellationToken>()).Returns(new List<ProductPermissionCatalogDto>
		{
			new()
			{
				Product = "logstream",
				PublishedAt = publishedAt,
				Permissions =
				[
					new PermissionDefinitionDto { Name = "logs:read", Description = "Ler logs", AlsoDeclaredBy = ["outro"] },
					new PermissionDefinitionDto { Name = "logs:write", Description = "Gravar logs", AlsoDeclaredBy = [] },
				],
			},
			new() { Product = "notificationhub", PublishedAt = publishedAt, Permissions = [] },
		});

		var catalog = await new SecureGatePermissionCatalogService(factory).ListAsync();

		catalog.Select(p => p.Product).Should().Equal("logstream", "notificationhub");
		catalog[0].PublishedAt.Should().Be(publishedAt);
		catalog[0].Permissions.Should().HaveCount(2);
		catalog[0].Permissions[0].Should().BeEquivalentTo(
			new PermissionEntry("logs:read", "Ler logs", ["outro"]));
		catalog[0].Permissions[1].AlsoDeclaredBy.Should().BeEmpty();
	}

	[Fact]
	public async Task ListarCatalogo_ErroDoClientPropaga()
	{
		var (factory, client) = BuildFactory();
		client.ListPermissionCatalogAsync(Arg.Any<CancellationToken>())
			.Returns<ICollection<ProductPermissionCatalogDto>>(_ => throw new ApiException("x", 403, "", null!, null));

		var act = () => new SecureGatePermissionCatalogService(factory).ListAsync();

		// A tradução em mensagem fica na página (ApiErrorFormatter), como nos demais serviços.
		(await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(403);
	}

	[Fact]
	public async Task RemoverOrfas_ChamaOTenantCertoEDevolveOResumo()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		client.RemoveUnknownRolePermissionsAsync(tenantId, Arg.Any<CancellationToken>()).Returns(
			new RemovedUnknownPermissionsDto
			{
				RolesChanged = 1,
				PermissionsRemoved = 2,
				Roles = [new RoleUnknownPermissionsDto { Role = "leitor", Removed = ["a:b", "c:d"] }],
			});

		var summary = await new SecureGateRoleAdminService(factory).RemoveUnknownPermissionsAsync(tenantId);

		summary.RolesChanged.Should().Be(1);
		summary.PermissionsRemoved.Should().Be(2);
		summary.Roles.Should().ContainSingle().Which.Should().BeEquivalentTo(
			new RoleUnknownPermissions("leitor", ["a:b", "c:d"]));
		await client.Received(1).RemoveUnknownRolePermissionsAsync(tenantId, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetRole_ProjetaPermissoesDesconhecidas()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		client.GetRoleAsync(tenantId, "leitor", Arg.Any<CancellationToken>()).Returns(new RoleDetailDto
		{
			Name = "leitor",
			Permissions = ["logs:read"],
			UnknownPermissions = ["velha:acao"],
			MemberCount = 1,
		});

		var role = await new SecureGateRoleAdminService(factory).GetRoleAsync(tenantId, "leitor");

		role.UnknownPermissions.Should().Equal("velha:acao");
	}
}
