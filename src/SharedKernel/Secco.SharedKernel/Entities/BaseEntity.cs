using Secco.SharedKernel.Exceptions;

namespace Secco.SharedKernel.Entities;

/// <summary>
/// Base de toda entidade da plataforma (ADR-0003): identidade por <see cref="Guid"/> v7
/// (ordenável no tempo — não fragmenta índice clusterizado no SQL Server, ADR-0018,
/// e dispensa coordenação entre bancos de tenant, ADR-0005), igualdade por Id + tipo
/// e acúmulo de eventos de domínio.
/// </summary>
public abstract class BaseEntity : IEquatable<BaseEntity>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Cria a entidade com um novo Id Guid v7.</summary>
    protected BaseEntity()
        : this(Guid.CreateVersion7())
    {
    }

    /// <summary>Cria a entidade com o Id informado (ex.: reidratação ou IDs determinísticos de seed, ADR-0019).</summary>
    /// <param name="id">Identificador da entidade.</param>
    /// <exception cref="DomainInvariantException">Se o Id for <see cref="Guid.Empty"/>.</exception>
    protected BaseEntity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainInvariantException("O Id de uma entidade não pode ser Guid.Empty.");
        }

        Id = id;
    }

    /// <summary>Identificador único da entidade (coluna <c>id_pk_*</c> via convention, ADR-0017).</summary>
    public Guid Id { get; private set; }

    /// <summary>Eventos de domínio acumulados e ainda não despachados.</summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    /// <summary>Compara entidades por igualdade de referência ou por tipo + Id.</summary>
    /// <param name="other">Entidade a comparar.</param>
    public bool Equals(BaseEntity? other) =>
        other is not null
        && (ReferenceEquals(this, other) || (other.GetType() == GetType() && other.Id == Id));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as BaseEntity);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    /// <summary>Compara duas entidades por igualdade (tipo + Id).</summary>
    /// <param name="left">Entidade à esquerda.</param>
    /// <param name="right">Entidade à direita.</param>
    public static bool operator ==(BaseEntity? left, BaseEntity? right) =>
        left?.Equals(right) ?? right is null;

    /// <summary>Compara duas entidades por desigualdade (tipo + Id).</summary>
    /// <param name="left">Entidade à esquerda.</param>
    /// <param name="right">Entidade à direita.</param>
    public static bool operator !=(BaseEntity? left, BaseEntity? right) => !(left == right);

    /// <summary>Registra um evento de domínio para despacho posterior.</summary>
    /// <param name="domainEvent">Evento ocorrido nesta entidade.</param>
    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        _domainEvents.Add(domainEvent);
    }

    /// <summary>Limpa os eventos acumulados — chamado pela infraestrutura após o despacho.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
