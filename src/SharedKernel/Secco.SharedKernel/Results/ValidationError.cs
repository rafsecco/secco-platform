namespace Secco.SharedKernel.Results;

/// <summary>
/// Erro de validação que agrega múltiplos <see cref="Error"/> individuais
/// (ex.: vários campos inválidos em um mesmo comando). O <see cref="Result{TValue}"/>
/// continua carregando um único <see cref="Error"/>; camadas externas detectam este
/// subtipo para expandir os erros individuais (ex.: campo <c>errors</c> do ProblemDetails).
/// </summary>
public sealed record ValidationError : Error
{
    /// <summary>Código estável do erro de validação agregado.</summary>
    public const string AggregateCode = "Validation.General";

    /// <summary>
    /// Cria um erro de validação agregando um ou mais erros individuais.
    /// </summary>
    /// <param name="errors">Erros individuais de validação. Deve conter ao menos um.</param>
    /// <exception cref="ArgumentException">Se <paramref name="errors"/> estiver vazio.</exception>
    public ValidationError(params IReadOnlyList<Error> errors)
        : base(AggregateCode, "Um ou mais erros de validação ocorreram.", ErrorType.Validation)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (errors.Count == 0)
        {
            throw new ArgumentException("Um ValidationError exige ao menos um erro individual.", nameof(errors));
        }

        Errors = errors;
    }

    /// <summary>Erros individuais de validação agregados.</summary>
    public IReadOnlyList<Error> Errors { get; }
}
