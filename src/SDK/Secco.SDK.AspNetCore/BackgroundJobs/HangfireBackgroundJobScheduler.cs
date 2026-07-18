using Hangfire;

namespace Secco.SDK.AspNetCore.BackgroundJobs;

/// <summary>Implementação de <see cref="IBackgroundJobScheduler"/> sobre o Hangfire (ADR-0015 Camada 2).</summary>
internal sealed class HangfireBackgroundJobScheduler(IBackgroundJobClient client) : IBackgroundJobScheduler
{
    public string Enqueue<TJob, TPayload>(Guid tenantId, TPayload payload)
        where TJob : IBackgroundJob<TPayload> =>
        client.Enqueue<TenantJobRunner<TJob, TPayload>>(
            runner => runner.RunAsync(tenantId, payload, CancellationToken.None));
}
