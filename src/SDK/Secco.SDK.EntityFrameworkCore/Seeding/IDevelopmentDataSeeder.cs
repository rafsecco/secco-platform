namespace Secco.SDK.EntityFrameworkCore.Seeding;

/// <summary>
/// Seed de <b>desenvolvimento</b> (ADR-0019): dados de amostra para navegar na aplicação
/// sem cadastro manual. Roda APENAS sob a guarda dupla — <c>IHostEnvironment.IsDevelopment()</c>
/// <b>e</b> flag <c>Secco:Seed:Development = true</c> — e sempre <b>após</b> os seeds de
/// referência (constrói sobre eles). Dados realistas com Bogus (locale <c>pt_BR</c>, seed
/// randômico fixo para reprodutibilidade) — o Bogus é referenciado pela Infrastructure do
/// produto, não pelo SDK.
/// </summary>
public interface IDevelopmentDataSeeder
{
    /// <summary>Ordem de execução entre seeders do mesmo tipo (menor executa antes).</summary>
    int Order => 0;

    /// <summary>Aplica o seed de dados de amostra.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task SeedAsync(CancellationToken cancellationToken = default);
}
