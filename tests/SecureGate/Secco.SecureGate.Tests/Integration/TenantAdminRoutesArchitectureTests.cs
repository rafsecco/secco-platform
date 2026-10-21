using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Api.Authorization;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// As rotas abertas à autoridade de admin de tenant (ADR-0044) são exatamente as da lista versionada
/// <c>Architecture/tenant-admin-routes.txt</c>, com a mesma proteção de alvo. Abrir uma rota é decisão de
/// segurança: sem este teste, ela passaria num diff de endpoint sem ninguém notar.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class TenantAdminRoutesArchitectureTests(SecureGateApiFactory factory)
{
	private const string ListPath = "tests/SecureGate/Secco.SecureGate.Tests/Architecture/tenant-admin-routes.txt";

	[Fact]
	public void RotasMarcadas_SaoExatamenteAsDaLista()
	{
		var actual = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
			.Select(endpoint => (
				Name: endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
				Allowed: endpoint.Metadata.GetMetadata<TenantAdminAllowedMetadata>()))
			.Where(x => x.Allowed is not null)
			.Select(x => $"{x.Name} | {(x.Allowed!.CreatesTenant ? "criacao" : x.Allowed.AnyGrant ? "qualquer" : x.Allowed.ProtectsAdminTargets ? "sim" : "nao")}")
			.Order(StringComparer.Ordinal)
			.ToList();

		var expected = File.ReadAllLines(Path.Combine(RepositoryRoot(), ListPath))
			.Select(line => line.Trim())
			.Where(line => line.Length > 0 && !line.StartsWith('#'))
			.Order(StringComparer.Ordinal)
			.ToList();

		actual.Should().Equal(expected, "abrir ou fechar rota a admin de tenant passa pela lista versionada (ADR-0044)");
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
