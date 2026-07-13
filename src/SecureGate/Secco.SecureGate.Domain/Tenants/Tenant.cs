using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.SecureGate.Domain.Tenants;

/// <summary>
/// Tenant da plataforma — o catálogo central (ADR-0005/0022) vive no banco do SecureGate
/// e é gerenciado pelo AdminPortal (Fase 7). Usuários e roles referenciam o tenant;
/// as connection strings por produto chegam na fase 6.3.
/// </summary>
public sealed class Tenant : BaseEntity
{
    private Tenant()
    {
        // Construtor de rehidratação do EF Core
        Name = string.Empty;
        Slug = string.Empty;
    }

    /// <summary>Cria um tenant ativo.</summary>
    /// <param name="name">Nome de exibição. Obrigatório.</param>
    /// <param name="slug">Identificador curto único (kebab-case). Obrigatório.</param>
    /// <exception cref="DomainInvariantException">Se nome ou slug forem nulos/vazios.</exception>
    public Tenant(string name, string slug)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainInvariantException("Um tenant exige nome não vazio.");
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new DomainInvariantException("Um tenant exige slug não vazio.");
        }

        Name = name;
        Slug = slug.ToLowerInvariant();
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Nome de exibição (coluna <c>ds_name</c>).</summary>
    public string Name { get; private set; }

    /// <summary>Identificador curto único (coluna <c>ds_slug</c>, índice único).</summary>
    public string Slug { get; private set; }

    /// <summary>Tenant ativo (coluna <c>fl_active</c>). Tenants inativos não autenticam.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Desativa o tenant — bloqueia autenticação de seus usuários.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Reativa o tenant.</summary>
    public void Activate() => IsActive = true;
}
