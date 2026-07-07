namespace Secco.SharedKernel.Entities;

/// <summary>
/// Marca um evento de domínio: um fato de negócio ocorrido em uma entidade (ADR-0002).
/// O despacho dos eventos é responsabilidade da Infrastructure/SDK — o kernel
/// apenas os acumula na <see cref="BaseEntity"/>.
/// </summary>
public interface IDomainEvent;
