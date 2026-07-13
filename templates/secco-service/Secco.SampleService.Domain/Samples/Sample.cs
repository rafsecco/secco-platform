using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.SampleService.Domain.Samples;

/// <summary>
/// Recurso de EXEMPLO do template: demonstra o padrão de entidade da plataforma
/// (BaseEntity Guid v7, imutabilidade seletiva, guardas de invariante, nomenclatura
/// por convention — ADR-0017). Apague a pasta Samples ao modelar o domínio real.
/// </summary>
public sealed class Sample : BaseEntity
{
    private Sample()
    {
        // Construtor de rehidratação do EF Core
        Name = string.Empty;
    }

    /// <summary>Cria um sample.</summary>
    /// <param name="name">Nome. Obrigatório.</param>
    /// <param name="description">Descrição livre, quando houver.</param>
    /// <exception cref="DomainInvariantException">Se o nome for nulo ou vazio.</exception>
    public Sample(string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainInvariantException("Um sample exige nome não vazio.");
        }

        Name = name;
        Description = description;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Nome (coluna <c>ds_name</c>).</summary>
    public string Name { get; private set; }

    /// <summary>Descrição (coluna <c>ds_description</c>).</summary>
    public string? Description { get; private set; }

    /// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
    public DateTimeOffset CreatedAt { get; private set; }
}
