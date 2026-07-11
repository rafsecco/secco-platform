using Microsoft.EntityFrameworkCore;
using Secco.SDK.EntityFrameworkCore.Conventions;

namespace Secco.SDK.EntityFrameworkCore;

/// <summary>
/// <c>DbContext</c> base de todo produto da plataforma: registra a
/// <see cref="SeccoNamingConvention"/> (ADR-0017) — nomes de tabela, coluna, constraint
/// e índice saem no padrão sem nenhuma digitação manual. Agnóstico de provider (ADR-0018).
/// Adotantes externos que não possam herdar registram a convention diretamente em
/// <c>ConfigureConventions</c>.
/// </summary>
public abstract class SeccoDbContext : DbContext
{
    /// <summary>Inicializa o contexto sem opções (design-time).</summary>
    protected SeccoDbContext()
    {
    }

    /// <summary>Inicializa o contexto com as opções fornecidas pelo DI.</summary>
    /// <param name="options">Opções de configuração do contexto.</param>
    protected SeccoDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Conventions.Add(_ => new SeccoNamingConvention());
    }
}
