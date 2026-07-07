using System.Diagnostics.CodeAnalysis;

namespace Secco.SharedKernel.Results;

/// <summary>
/// Erro de negócio com código estável, descrição legível e categoria semântica (ADR-0004).
/// </summary>
/// <param name="Code">Código estável no formato <c>Produto.Recurso.Motivo</c> (ex.: <c>Platform.Tenant.NotResolved</c>).</param>
/// <param name="Description">Descrição legível do erro, voltada ao consumidor da API.</param>
/// <param name="Type">Categoria semântica do erro (<see cref="ErrorType"/>).</param>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Justification = "'Error' é o nome canônico do tipo na ADR-0003; a colisão é com palavra reservada do VB, que não é linguagem suportada pela plataforma.")]
public record Error(string Code, string Description, ErrorType Type)
{
    /// <summary>Ausência de erro — usado exclusivamente por resultados de sucesso.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    /// <summary>Cria um erro de entrada inválida (<see cref="ErrorType.Validation"/>).</summary>
    /// <param name="code">Código estável do erro.</param>
    /// <param name="description">Descrição legível do erro.</param>
    public static Error Validation(string code, string description) =>
        new(code, description, ErrorType.Validation);

    /// <summary>Cria um erro de recurso não encontrado (<see cref="ErrorType.NotFound"/>).</summary>
    /// <param name="code">Código estável do erro.</param>
    /// <param name="description">Descrição legível do erro.</param>
    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound);

    /// <summary>Cria um erro de conflito de estado (<see cref="ErrorType.Conflict"/>).</summary>
    /// <param name="code">Código estável do erro.</param>
    /// <param name="description">Descrição legível do erro.</param>
    public static Error Conflict(string code, string description) =>
        new(code, description, ErrorType.Conflict);

    /// <summary>Cria um erro de chamador não autenticado (<see cref="ErrorType.Unauthorized"/>).</summary>
    /// <param name="code">Código estável do erro.</param>
    /// <param name="description">Descrição legível do erro.</param>
    public static Error Unauthorized(string code, string description) =>
        new(code, description, ErrorType.Unauthorized);

    /// <summary>Cria um erro de chamador sem permissão (<see cref="ErrorType.Forbidden"/>).</summary>
    /// <param name="code">Código estável do erro.</param>
    /// <param name="description">Descrição legível do erro.</param>
    public static Error Forbidden(string code, string description) =>
        new(code, description, ErrorType.Forbidden);

    /// <summary>Cria uma falha de negócio genérica (<see cref="ErrorType.Failure"/>).</summary>
    /// <param name="code">Código estável do erro.</param>
    /// <param name="description">Descrição legível do erro.</param>
    public static Error Failure(string code, string description) =>
        new(code, description, ErrorType.Failure);
}
