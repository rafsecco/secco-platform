using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Api.Authorization;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// As rotas abertas à gestão do próprio tenant por máquina (ADR-0052) são exatamente as da lista versionada
/// <c>Architecture/tenant-manage-routes.txt</c>, com a mesma proteção de alvo — e toda rota aberta a ela também é
/// aberta a admin de tenant, porque a policy dos grupos e a conferência de alvo são as mesmas.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class TenantManageRoutesArchitectureTests(SecureGateApiFactory factory)
{
	private const string ListPath = "tests/SecureGate/Secco.SecureGate.Tests/Architecture/tenant-manage-routes.txt";

	private IReadOnlyList<Endpoint> MarkedEndpoints() =>
		[.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
			.Where(endpoint => endpoint.Metadata.GetMetadata<TenantManageAllowedMetadata>() is not null)];

	[Fact]
	public void RotasMarcadas_SaoExatamenteAsDaLista()
	{
		var actual = MarkedEndpoints()
			.Select(endpoint => $"{endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName} | "
				+ (endpoint.Metadata.GetMetadata<TenantManageAllowedMetadata>()!.ProtectsAdminTargets ? "sim" : "nao"))
			.Order(StringComparer.Ordinal)
			.ToList();

		var expected = File.ReadAllLines(Path.Combine(RepositoryRoot(), ListPath))
			.Select(line => line.Trim())
			.Where(line => line.Length > 0 && !line.StartsWith('#'))
			.Order(StringComparer.Ordinal)
			.ToList();

		actual.Should().Equal(expected, "abrir ou fechar rota à gestão do tenant passa pela lista versionada (ADR-0052)");
	}

	[Fact]
	public void RotasMarcadas_NuncaCriamTenantNemLeemSemTenant()
	{
		MarkedEndpoints()
			.Select(endpoint => endpoint.Metadata.GetMetadata<TenantAdminAllowedMetadata>())
			.Should().NotContain(allowed => allowed != null && (allowed.CreatesTenant || allowed.AnyGrant));
	}

	[Fact]
	public void LeituraDaTrilhaDeAutoridade_NaoEstaAbertaAAdminDeTenantNemAGestaoPorMaquina()
	{
		var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
			.Single(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "ListTenantAuditEntries");

		endpoint.Metadata.GetMetadata<TenantManageAllowedMetadata>().Should().BeNull("a trilha existe contra a máquina (ADR-0054)");
		endpoint.Metadata.GetMetadata<TenantAdminAllowedMetadata>().Should().BeNull("só o operador lê a trilha (ADR-0054)");
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);

		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Secco.Platform.slnx")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
	}
}
