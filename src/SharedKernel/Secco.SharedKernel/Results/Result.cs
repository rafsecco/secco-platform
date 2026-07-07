namespace Secco.SharedKernel.Results;

/// <summary>
/// Resultado de uma operação de negócio sem valor de retorno (ADR-0004).
/// Erros de negócio fluem por este tipo — nunca por exceções, que ficam
/// reservadas a falhas de infraestrutura e bugs.
/// </summary>
public class Result
{
    /// <summary>
    /// Inicializa o resultado garantindo o invariante: sucesso nunca carrega erro
    /// e falha sempre carrega um erro diferente de <see cref="Error.None"/>.
    /// </summary>
    /// <param name="isSuccess">Indica se a operação foi bem-sucedida.</param>
    /// <param name="error">Erro da operação; <see cref="Error.None"/> em caso de sucesso.</param>
    /// <exception cref="ArgumentException">Se a combinação violar o invariante.</exception>
    protected Result(bool isSuccess, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("Um resultado de sucesso não pode carregar erro.", nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException("Um resultado de falha exige um erro.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Indica se a operação foi bem-sucedida.</summary>
    public bool IsSuccess { get; }

    /// <summary>Indica se a operação falhou.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Erro da operação; <see cref="Error.None"/> quando <see cref="IsSuccess"/>.</summary>
    public Error Error { get; }

    /// <summary>Cria um resultado de sucesso sem valor.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>Cria um resultado de falha com o erro informado.</summary>
    /// <param name="error">Erro de negócio ocorrido.</param>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Cria um resultado de sucesso carregando o valor informado.</summary>
    /// <typeparam name="TValue">Tipo do valor produzido pela operação.</typeparam>
    /// <param name="value">Valor produzido pela operação.</param>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    /// <summary>Cria um resultado de falha tipado com o erro informado.</summary>
    /// <typeparam name="TValue">Tipo do valor que a operação produziria em caso de sucesso.</typeparam>
    /// <param name="error">Erro de negócio ocorrido.</param>
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}
