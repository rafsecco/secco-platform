using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.AspNetCore.Extensions;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.BackgroundJobs;

/// <summary>
/// Fora de Development o schema do Hangfire é aplicado pelo verbo <c>migrate</c>; o servidor
/// precisa poder subir sem preparar o schema (ADR-0038).
/// </summary>
public class SeccoBackgroundJobsPrepareSchemaTests
{
	// Porta 1 em loopback recusa a conexão na hora; qualquer construção que toque o banco falha.
	private const string UnreachableServer = "Server=127.0.0.1,1;Database=x;Connect Timeout=1;Encrypt=False";

	private static JobStorage ResolveStorage(bool prepareSchema)
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSeccoBackgroundJobs(_ => UnreachableServer, prepareSchema);
		using var provider = services.BuildServiceProvider();
		return provider.GetRequiredService<JobStorage>();
	}

	[Fact]
	public void AddSeccoBackgroundJobs_PrepareSchemaFalse_NaoTocaNoBanco()
	{
		var act = () => ResolveStorage(prepareSchema: false);

		act.Should().NotThrow();
	}

	[Fact]
	public void AddSeccoBackgroundJobs_PrepareSchemaTrue_TentaConectarELanca()
	{
		var act = () => ResolveStorage(prepareSchema: true);

		act.Should().Throw<Exception>();
	}
}
