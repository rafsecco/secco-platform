using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.SDK.AspNetCore.BackgroundJobs;

/// <summary>
/// Ponte entre o Hangfire e o job real: é este método (não o job) que o Hangfire de fato
/// enfileira e invoca. Restaura o tenant no escopo (ADR-0005) ANTES de resolver e chamar
/// o job — nenhum job precisa lembrar de chamar SetTenant sozinho. Retry automático em
/// falha transitória (ADR-0015 Camada 2) — igual para todo job da plataforma; um produto
/// que precise de política diferente ajusta via <c>GlobalJobFilters</c> no próprio host.
/// </summary>
[AutomaticRetry(Attempts = 5, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
internal sealed class TenantJobRunner<TJob, TPayload>(IServiceProvider serviceProvider)
    where TJob : IBackgroundJob<TPayload>
{
    public async Task RunAsync(Guid tenantId, TPayload payload, CancellationToken cancellationToken)
    {
        serviceProvider.SetTenant(tenantId);

        var job = serviceProvider.GetRequiredService<TJob>();
        await job.ExecuteAsync(payload, cancellationToken).ConfigureAwait(false);
    }
}
