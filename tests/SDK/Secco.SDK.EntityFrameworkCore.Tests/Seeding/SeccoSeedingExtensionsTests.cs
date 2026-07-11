using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Xunit;

namespace Secco.SDK.EntityFrameworkCore.Tests.Seeding;

public class SeccoSeedingExtensionsTests
{
    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Secco.Tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingReferenceSeeder(List<string> executionLog, string name, int order) : IReferenceDataSeeder
    {
        public int Order => order;

        public Task SeedAsync(CancellationToken cancellationToken = default)
        {
            executionLog.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDevelopmentSeeder(List<string> executionLog, string name) : IDevelopmentDataSeeder
    {
        public Task SeedAsync(CancellationToken cancellationToken = default)
        {
            executionLog.Add(name);
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider BuildProvider(
        List<string> executionLog,
        string environmentName = "Production",
        bool? developmentFlag = null,
        bool registerEnvironment = true)
    {
        var services = new ServiceCollection();

        if (registerEnvironment)
        {
            services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(environmentName));
        }

        if (developmentFlag is not null)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [SeccoSeedingExtensions.DevelopmentSeedFlagKey] = developmentFlag.Value.ToString(),
                })
                .Build();

            services.AddSingleton<IConfiguration>(configuration);
        }

        services.AddScoped<IReferenceDataSeeder>(_ => new RecordingReferenceSeeder(executionLog, "ref-segundo", order: 2));
        services.AddScoped<IReferenceDataSeeder>(_ => new RecordingReferenceSeeder(executionLog, "ref-primeiro", order: 1));
        services.AddScoped<IDevelopmentDataSeeder>(_ => new RecordingDevelopmentSeeder(executionLog, "dev"));

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SeedSeccoDataAsync_Always_RunsReferenceSeedersOrderedByOrder()
    {
        var executionLog = new List<string>();
        await using var provider = BuildProvider(executionLog);

        await provider.SeedSeccoDataAsync();

        executionLog.Should().ContainInOrder("ref-primeiro", "ref-segundo");
    }

    [Fact]
    public async Task SeedSeccoDataAsync_InDevelopmentWithFlag_RunsDevelopmentAfterReference()
    {
        var executionLog = new List<string>();
        await using var provider = BuildProvider(executionLog, environmentName: "Development", developmentFlag: true);

        await provider.SeedSeccoDataAsync();

        executionLog.Should().Equal("ref-primeiro", "ref-segundo", "dev");
    }

    [Fact]
    public async Task SeedSeccoDataAsync_InDevelopmentWithoutFlag_SkipsDevelopmentSeeders()
    {
        var executionLog = new List<string>();
        await using var provider = BuildProvider(executionLog, environmentName: "Development", developmentFlag: null);

        await provider.SeedSeccoDataAsync();

        executionLog.Should().NotContain("dev");
    }

    [Fact]
    public async Task SeedSeccoDataAsync_InProductionEvenWithFlag_SkipsDevelopmentSeeders()
    {
        var executionLog = new List<string>();
        await using var provider = BuildProvider(executionLog, environmentName: "Production", developmentFlag: true);

        await provider.SeedSeccoDataAsync();

        executionLog.Should().NotContain("dev", "flag ligada em produção não basta: a guarda é dupla (ADR-0019)");
    }

    [Fact]
    public async Task SeedSeccoDataAsync_WithoutHostEnvironment_SkipsDevelopmentSeedersFailClosed()
    {
        var executionLog = new List<string>();
        await using var provider = BuildProvider(executionLog, developmentFlag: true, registerEnvironment: false);

        await provider.SeedSeccoDataAsync();

        executionLog.Should().NotContain("dev", "sem IHostEnvironment não há como provar que é DEV — fail-closed");
        executionLog.Should().HaveCount(2, "os seeds de referência rodam em qualquer ambiente");
    }
}
