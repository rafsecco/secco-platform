namespace Secco.NotificationHub.Infrastructure;

/// <summary>
/// Connection string do banco de PLATAFORMA do Hangfire (seção
/// <c>NotificationHub:BackgroundJobs</c>) — nunca o banco por tenant (ADR-0015: os jobs
/// não pertencem a um tenant específico; o <c>tenant_id</c> viaja no payload).
/// </summary>
public sealed class NotificationHubBackgroundJobOptions
{
    /// <summary>Connection string do banco de plataforma (schema próprio, gerenciado pelo Hangfire).</summary>
    public string ConnectionString { get; set; } = string.Empty;
}
