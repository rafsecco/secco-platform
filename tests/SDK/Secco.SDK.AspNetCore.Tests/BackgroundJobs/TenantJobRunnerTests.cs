using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.AspNetCore.BackgroundJobs;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.BackgroundJobs;

/// <summary>
/// <see cref="TenantJobRunner{TJob, TPayload}"/> é a ponte entre o Hangfire e o job real
/// (ADR-0015 Camada 2) — este teste prova a garantia central: o tenant é restaurado
/// (ADR-0005) ANTES do job rodar, sem o job precisar saber disso.
/// </summary>
public class TenantJobRunnerTests
{
    private sealed record FakePayload(string Value);

    private sealed class FakeJob : IBackgroundJob<FakePayload>
    {
        public Guid? TenantIdSeenDuringExecution { get; private set; }

        public string? PayloadSeenDuringExecution { get; private set; }

        public Task ExecuteAsync(FakePayload payload, CancellationToken cancellationToken)
        {
            TenantIdSeenDuringExecution = CapturedTenantContext?.TenantId;
            PayloadSeenDuringExecution = payload.Value;
            return Task.CompletedTask;
        }

        public ITenantContext? CapturedTenantContext { get; set; }
    }

    [Fact]
    public async Task RunAsync_RestoresTenantBeforeInvokingTheJob()
    {
        var tenantId = Guid.NewGuid();
        var job = new FakeJob();

        var services = new ServiceCollection();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<FakeJob>(_ => job);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // O próprio fake lê o TenantContext do MESMO escopo, simulando um repositório
        // que resolve o tenant preguiçosamente (padrão real dos repositórios da plataforma)
        job.CapturedTenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        var runner = new TenantJobRunner<FakeJob, FakePayload>(scope.ServiceProvider);

        await runner.RunAsync(tenantId, new FakePayload("conteúdo"), CancellationToken.None);

        job.TenantIdSeenDuringExecution.Should().Be(tenantId);
        job.PayloadSeenDuringExecution.Should().Be("conteúdo");
    }
}
