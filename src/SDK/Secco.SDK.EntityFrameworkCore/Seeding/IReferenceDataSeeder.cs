namespace Secco.SDK.EntityFrameworkCore.Seeding;

/// <summary>
/// Seed de <b>referência</b> (ADR-0019): dados obrigatórios para o sistema funcionar
/// (valores default de enums dinâmicos, registros de sistema, configurações padrão).
/// Roda em TODOS os ambientes — a implementação deve ser <b>idempotente</b> (upsert por
/// chave natural, IDs determinísticos): reexecutar nunca duplica nem corrompe.
/// Vive em <c>Infrastructure/Seeding/</c> do produto e integra o provisionamento de
/// cada tenant, reexecutando após cada migration (ADR-0005).
/// </summary>
public interface IReferenceDataSeeder
{
    /// <summary>Ordem de execução entre seeders do mesmo tipo (menor executa antes).</summary>
    int Order => 0;

    /// <summary>Aplica o seed de forma idempotente.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task SeedAsync(CancellationToken cancellationToken = default);
}
