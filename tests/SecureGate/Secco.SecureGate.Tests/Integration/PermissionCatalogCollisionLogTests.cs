using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.PermissionCatalog;
using Secco.SharedKernel.Authorization;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// A colisão de nome entre produtos é logada pelo repositório (Infrastructure, ADR-0002), só com
/// produto e nome — nunca a descrição (ADR-0020) — e não impede a publicação (ADR-0039).
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class PermissionCatalogCollisionLogTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private sealed class CapturingLogger : ILogger<PermissionCatalogRepository>
	{
		public List<(LogLevel Level, string Message)> Entries { get; } = [];

		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			Entries.Add((logLevel, formatter(state, exception)));
	}

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private static string NewProduct() => $"p-{Guid.NewGuid():N}"[..24];

	private async Task<IReadOnlyList<string>> ReplaceAsync(string product, string name, string description, CapturingLogger logger)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var repository = new PermissionCatalogRepository(context, logger);

		return await repository.ReplaceAsync(product, [new SeccoPermissionDefinition(name, description)], DateTimeOffset.UtcNow);
	}

	[Fact]
	public async Task ReplaceAsync_NameDeclaredByAnotherProduct_LogsWarningWithProductAndNameOnly_AndPublishes()
	{
		var name = $"res-{Guid.NewGuid():N}"[..16] + ":read";
		var other = NewProduct();
		var product = NewProduct();
		await ReplaceAsync(other, name, "primeira", new CapturingLogger());
		var logger = new CapturingLogger();

		var collisions = await ReplaceAsync(product, name, "SEGREDO-DA-DESCRICAO-INTEIRA", logger);

		collisions.Should().Equal([name]);
		var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
		warning.Message.Should().Contain(product).And.Contain(name);
		warning.Message.Should().NotContain("SEGREDO-DA-DESCRICAO-INTEIRA");
	}

	[Fact]
	public async Task ReplaceAsync_WithoutCollision_LogsNoWarning()
	{
		var logger = new CapturingLogger();

		var collisions = await ReplaceAsync(NewProduct(), $"res-{Guid.NewGuid():N}"[..16] + ":read", "Ler", logger);

		collisions.Should().BeEmpty();
		logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
	}
}
