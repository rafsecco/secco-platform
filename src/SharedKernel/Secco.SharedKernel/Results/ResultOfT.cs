namespace Secco.SharedKernel.Results;

/// <summary>
/// Resultado de uma operação de negócio que produz um valor (ADR-0004).
/// </summary>
/// <typeparam name="TValue">Tipo do valor produzido em caso de sucesso.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>
    /// Valor produzido pela operação. Acessá-lo em um resultado de falha é bug do
    /// chamador — verifique <see cref="Result.IsSuccess"/> antes.
    /// </summary>
    /// <exception cref="InvalidOperationException">Se o resultado for de falha.</exception>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"O valor de um resultado de falha não pode ser acessado (erro: {Error.Code}).");

    /// <summary>Converte um valor em resultado de sucesso.</summary>
    /// <param name="value">Valor produzido pela operação.</param>
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    /// <summary>Converte um erro em resultado de falha.</summary>
    /// <param name="error">Erro de negócio ocorrido.</param>
    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);
}
