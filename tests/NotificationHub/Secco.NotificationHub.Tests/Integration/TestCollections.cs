using Xunit;

namespace Secco.NotificationHub.Tests.Integration;

/// <summary>
/// Collection compartilhada: o Hangfire usa um bridge de log estático por processo
/// (Hangfire.Logging.LogProvider) — múltiplas instâncias de <see cref="NotificationHubApiFactory"/>
/// no mesmo processo de teste quebram quando a primeira é descartada (ObjectDisposedException
/// no LoggerFactory capturado). Uma única factory para todas as classes evita o problema.
/// </summary>
[CollectionDefinition(Name)]
public sealed class NotificationHubApiCollectionDefinition : ICollectionFixture<NotificationHubApiFactory>
{
    /// <summary>Nome da collection.</summary>
    public const string Name = "NotificationHub API compartilhada";
}
