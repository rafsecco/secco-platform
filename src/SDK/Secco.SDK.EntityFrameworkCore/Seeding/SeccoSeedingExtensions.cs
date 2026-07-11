using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Secco.SDK.EntityFrameworkCore.Seeding;

/// <summary>
/// Orquestração de seeding da plataforma (ADR-0019). Disparo sempre <b>explícito</b> —
/// <c>Program.cs</c> em DEV, pipeline de provisionamento de tenant, job pós-migration
/// (ADR-0005: processo controlado, nunca efeito automático de startup em produção).
/// </summary>
public static class SeccoSeedingExtensions
{
    /// <summary>Flag de configuração da guarda dupla do seed de desenvolvimento.</summary>
    public const string DevelopmentSeedFlagKey = "Secco:Seed:Development";

    /// <summary>
    /// Executa os seeds registrados no DI: primeiro todos os <see cref="IReferenceDataSeeder"/>
    /// (ordenados por <c>Order</c>), depois — somente sob a guarda dupla
    /// (<c>IsDevelopment()</c> E <c>Secco:Seed:Development = true</c>; fail-closed na ausência
    /// de qualquer uma) — os <see cref="IDevelopmentDataSeeder"/>.
    /// </summary>
    /// <param name="serviceProvider">Raiz de serviços da aplicação (um escopo próprio é criado).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public static async Task SeedSeccoDataAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        await using var scope = serviceProvider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(SeccoSeedingExtensions))
            ?? NullLogger.Instance;

        var referenceSeeders = services.GetServices<IReferenceDataSeeder>()
            .OrderBy(seeder => seeder.Order)
            .ToList();

        SeedingLog.RunningReferenceSeeders(logger, referenceSeeders.Count);

        foreach (var seeder in referenceSeeders)
        {
            await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!IsDevelopmentSeedEnabled(services, logger))
        {
            return;
        }

        var developmentSeeders = services.GetServices<IDevelopmentDataSeeder>()
            .OrderBy(seeder => seeder.Order)
            .ToList();

        SeedingLog.RunningDevelopmentSeeders(logger, developmentSeeders.Count);

        foreach (var seeder in developmentSeeders)
        {
            await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Guarda dupla da ADR-0019, fail-closed: sem <c>IHostEnvironment</c> registrado
    /// (ex.: console puro) ou fora de Development, o seed de desenvolvimento não roda;
    /// em Development sem a flag explícita, também não — e o skip é logado para o dev
    /// entender por que o banco está vazio.
    /// </summary>
    private static bool IsDevelopmentSeedEnabled(IServiceProvider services, ILogger logger)
    {
        var environment = services.GetService<IHostEnvironment>();

        if (environment is null || !environment.IsDevelopment())
        {
            return false;
        }

        var configuration = services.GetService<IConfiguration>();
        var flagValue = configuration?[DevelopmentSeedFlagKey];

        if (!bool.TryParse(flagValue, out var enabled) || !enabled)
        {
            SeedingLog.DevelopmentSeedSkipped(logger, DevelopmentSeedFlagKey);
            return false;
        }

        return true;
    }
}

/// <summary>Mensagens de log estruturadas do seeding (source generator — ADR-0008).</summary>
internal static partial class SeedingLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Seed de referência: executando {SeederCount} seeder(s).")]
    public static partial void RunningReferenceSeeders(ILogger logger, int seederCount);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Seed de desenvolvimento: guarda dupla satisfeita — executando {SeederCount} seeder(s).")]
    public static partial void RunningDevelopmentSeeders(ILogger logger, int seederCount);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Seed de desenvolvimento pulado: ambiente é Development mas a flag '{FlagKey}' não está habilitada (guarda dupla, ADR-0019).")]
    public static partial void DevelopmentSeedSkipped(ILogger logger, string flagKey);
}
