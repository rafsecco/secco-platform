namespace Secco.SharedKernel.Entities;

/// <summary>
/// Marca uma entidade como logicamente excluível: o registro permanece no banco e é
/// filtrado das consultas por query filter global aplicado pelo Secco.SDK. Nem toda
/// entidade auditável é soft-deletable — implementar apenas quando exclusão física
/// não for aceitável.
/// </summary>
public interface ISoftDeletable
{
    /// <summary>Indica exclusão lógica (coluna <c>fl_deleted</c>, ADR-0017).</summary>
    bool IsDeleted { get; set; }

    /// <summary>Momento da exclusão lógica; nulo se ativa (coluna <c>dt_deleted_at</c>).</summary>
    DateTimeOffset? DeletedAt { get; set; }
}
