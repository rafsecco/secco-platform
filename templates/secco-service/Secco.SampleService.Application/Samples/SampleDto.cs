using Secco.SampleService.Domain.Samples;

namespace Secco.SampleService.Application.Samples;

/// <summary>Representação de leitura de um sample — a entidade nunca cruza a borda HTTP.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nome.</param>
/// <param name="Description">Descrição, quando houver.</param>
/// <param name="CreatedAt">Momento da criação.</param>
public sealed record SampleDto(Guid Id, string Name, string? Description, DateTimeOffset CreatedAt)
{
    /// <summary>Projeta a entidade para o DTO.</summary>
    public static SampleDto FromEntity(Sample entity) =>
        new(entity.Id, entity.Name, entity.Description, entity.CreatedAt);
}
