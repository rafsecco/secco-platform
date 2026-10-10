using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Xunit;

namespace Secco.SDK.EntityFrameworkCore.Tests.Seeding;

public class SeccoSeedingServiceCollectionExtensionsTests
{
	private sealed class ScopedMarker;

	private sealed class RecordingSeeder(List<string> log, string name) : IReferenceDataSeeder
	{
		public Task SeedAsync(CancellationToken cancellationToken = default)
		{
			log.Add(name);
			return Task.CompletedTask;
		}
	}

	[Fact]
	public async Task AddSeccoReferenceSeeder_WithDelegates_RunsInOrderAlongsideRegularSeeders()
	{
		var log = new List<string>();
		var services = new ServiceCollection();
		services.AddScoped<IReferenceDataSeeder>(_ => new RecordingSeeder(log, "regular-0"));
		services.AddSeccoReferenceSeeder((_, _) => { log.Add("delegate-10"); return Task.CompletedTask; }, order: 10);
		services.AddSeccoReferenceSeeder((_, _) => { log.Add("delegate-5"); return Task.CompletedTask; }, order: 5);
		await using var provider = services.BuildServiceProvider();

		await provider.SeedSeccoDataAsync();

		log.Should().Equal("regular-0", "delegate-5", "delegate-10");
	}

	[Fact]
	public async Task AddSeccoReferenceSeeder_Delegate_ReceivesScopedServiceProvider()
	{
		var services = new ServiceCollection();
		services.AddScoped<ScopedMarker>();
		ScopedMarker? first = null;
		ScopedMarker? second = null;
		services.AddSeccoReferenceSeeder((sp, _) => { first = sp.GetRequiredService<ScopedMarker>(); return Task.CompletedTask; });
		services.AddSeccoReferenceSeeder((sp, _) => { second = sp.GetRequiredService<ScopedMarker>(); return Task.CompletedTask; });
		await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

		await provider.SeedSeccoDataAsync();

		first.Should().NotBeNull();
		second.Should().BeSameAs(first);
	}
}
