namespace Secco.SDK.AspNetCore.BackgroundJobs;

/// <summary>
/// Corpo de um job de background persistente com retry (ADR-0015 Camada 2) — resolvido
/// via DI a cada execução. Quando <see cref="ExecuteAsync"/> roda, o tenant já foi
/// restaurado no escopo (ADR-0005): o job não precisa (e não deve) chamar SetTenant.
/// </summary>
/// <typeparam name="TPayload">Tipo do payload de entrada, serializado pelo storage do Hangfire.</typeparam>
public interface IBackgroundJob<in TPayload>
{
    /// <summary>Executa o job.</summary>
    /// <param name="payload">Dados de entrada do job.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task ExecuteAsync(TPayload payload, CancellationToken cancellationToken);
}
