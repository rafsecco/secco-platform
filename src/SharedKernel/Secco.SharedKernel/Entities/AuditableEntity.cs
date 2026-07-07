namespace Secco.SharedKernel.Entities;

/// <summary>
/// Entidade com trilha de auditoria de criação e alteração. Os campos são preenchidos
/// exclusivamente pelo interceptor de EF Core do Secco.SDK a partir do usuário autenticado
/// (claim <c>sub</c>) — nunca manualmente pelos produtos.
/// </summary>
public abstract class AuditableEntity : BaseEntity
{
    /// <summary>Cria a entidade com um novo Id Guid v7.</summary>
    protected AuditableEntity()
    {
    }

    /// <summary>Cria a entidade com o Id informado.</summary>
    /// <param name="id">Identificador da entidade.</param>
    protected AuditableEntity(Guid id)
        : base(id)
    {
    }

    /// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Identificador de quem criou — claim <c>sub</c> (coluna <c>ds_created_by</c>).</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Momento da última alteração; nulo se nunca alterada (coluna <c>dt_updated_at</c>).</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Identificador de quem alterou por último — claim <c>sub</c> (coluna <c>ds_updated_by</c>).</summary>
    public string? UpdatedBy { get; set; }
}
