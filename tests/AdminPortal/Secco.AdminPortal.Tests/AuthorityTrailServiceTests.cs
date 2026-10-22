using FluentAssertions;
using NSubstitute;
using Secco.AdminPortal.Services;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>
/// Trilha de autoridade do tenant no portal (ADR-0054): o serviço lê a rota nova com o client do operador e
/// projeta a página, sem repassar a Metadata crua.
/// </summary>
public class AuthorityTrailServiceTests
{
	private static readonly Guid Tenant = Guid.NewGuid();

	[Fact]
	public async Task ListarTrilha_PedeAPaginaEProjetaAsEntradas()
	{
		var client = Substitute.For<ISecureGateClient>();
		var factory = Substitute.For<ISecureGateClientFactory>();
		factory.CreateAsync(Arg.Any<CancellationToken>()).Returns(client);
		var id = Guid.NewGuid();
		var at = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
		client.ListTenantAuditEntriesAsync(Tenant, 2, 25, Arg.Any<CancellationToken>()).Returns(new PagedResultOfAuthorityAuditEntryDto
		{
			Items = new List<AuthorityAuditEntryDto>
			{
				new()
				{
					Id = id, ActorType = "client", ActorId = "cli_intranet", Action = "gestao-do-tenant.CreateRole",
					ResourceType = "tenant", ResourceId = Tenant.ToString(), Metadata = "{}", OccurredAt = at,
				},
			},
			Page = 2,
			Size = 25,
			TotalCount = 26,
			TotalPages = 2,
		});

		var page = await new SecureGateTenantAdminService(factory).ListAuthorityTrailAsync(Tenant, 2, 25);

		page.Page.Should().Be(2);
		page.TotalCount.Should().Be(26);
		page.TotalPages.Should().Be(2);
		page.Items.Should().ContainSingle().Which.Should().Be(
			new AuthorityTrailEntry(id, "client", "cli_intranet", "gestao-do-tenant.CreateRole", "tenant", Tenant.ToString(), at));
	}

	[Fact]
	public async Task ListarTrilha_ErroDoSecureGate_Propaga()
	{
		var client = Substitute.For<ISecureGateClient>();
		var factory = Substitute.For<ISecureGateClientFactory>();
		factory.CreateAsync(Arg.Any<CancellationToken>()).Returns(client);
		client.ListTenantAuditEntriesAsync(Tenant, 1, 25, Arg.Any<CancellationToken>())
			.Returns<PagedResultOfAuthorityAuditEntryDto>(_ => throw new ApiException("nope", 403, "x", new Dictionary<string, IEnumerable<string>>(), null));

		var act = () => new SecureGateTenantAdminService(factory).ListAuthorityTrailAsync(Tenant, 1, 25);

		(await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(403);
	}
}
