using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.EntityFrameworkCore.Migrations;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Xunit;

namespace Secco.SDK.EntityFrameworkCore.Tests.Migrations;

public class SeccoMigrationExtensionsTests
{
	private sealed class RecordingMigrator(List<string> log, IReadOnlyList<string> failures, bool @throw = false) : ISeccoDatabaseMigrator
	{
		public string Name => "teste";

		public Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default)
		{
			log.Add("migrate");
			return @throw ? throw new InvalidOperationException("boom") : Task.FromResult(failures);
		}
	}

	private sealed class RecordingSeeder(List<string> log) : IReferenceDataSeeder
	{
		public Task SeedAsync(CancellationToken cancellationToken = default)
		{
			log.Add("seed");
			return Task.CompletedTask;
		}
	}

	private static ServiceProvider Build(List<string> log, params ISeccoDatabaseMigrator[] migrators)
	{
		var services = new ServiceCollection();

		foreach (var migrator in migrators)
		{
			services.AddSingleton(migrator);
		}

		services.AddSingleton<IReferenceDataSeeder>(new RecordingSeeder(log));

		return services.BuildServiceProvider();
	}

	[Fact]
	public async Task RunSeccoMigrationsAsync_TudoOk_MigraDepoisSemeiaETrue()
	{
		var log = new List<string>();
		await using var provider = Build(log, new RecordingMigrator(log, []), new RecordingMigrator(log, []));

		var ok = await provider.RunSeccoMigrationsAsync();

		ok.Should().BeTrue();
		log.Should().Equal("migrate", "migrate", "seed");
	}

	[Fact]
	public async Task RunSeccoMigrationsAsync_MigratorComFalha_NaoSemeiaEFalse()
	{
		var log = new List<string>();
		await using var provider = Build(log, new RecordingMigrator(log, ["tenant-x"]), new RecordingMigrator(log, []));

		var ok = await provider.RunSeccoMigrationsAsync();

		ok.Should().BeFalse();
		log.Should().Equal("migrate", "migrate");
	}

	[Fact]
	public async Task RunSeccoMigrationsAsync_MigratorLanca_NaoSemeiaEFalse()
	{
		var log = new List<string>();
		await using var provider = Build(log, new RecordingMigrator(log, [], @throw: true));

		var ok = await provider.RunSeccoMigrationsAsync();

		ok.Should().BeFalse();
		log.Should().NotContain("seed");
	}

	[Fact]
	public async Task RunSeccoMigrationsAsync_SemMigrators_SemeiaETrue()
	{
		var log = new List<string>();
		await using var provider = Build(log);

		var ok = await provider.RunSeccoMigrationsAsync();

		ok.Should().BeTrue();
		log.Should().Equal("seed");
	}
}
