namespace Secco.SharedKernel.Results;

/// <summary>
/// Superfície funcional mínima do <see cref="Result"/>: <c>Match</c> (consumo na borda),
/// <c>Map</c> (transformação do valor) e <c>Bind</c> (encadeamento de operações que
/// retornam <see cref="Result"/>), com variantes assíncronas.
/// </summary>
public static class ResultExtensions
{
    /// <summary>Consome o resultado, invocando o ramo correspondente ao seu estado.</summary>
    /// <typeparam name="TOut">Tipo produzido pelos ramos.</typeparam>
    /// <param name="result">Resultado a consumir.</param>
    /// <param name="onSuccess">Ramo executado em caso de sucesso.</param>
    /// <param name="onFailure">Ramo executado em caso de falha, recebendo o erro.</param>
    public static TOut Match<TOut>(this Result result, Func<TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return result.IsSuccess ? onSuccess() : onFailure(result.Error);
    }

    /// <summary>Consome o resultado, invocando o ramo correspondente ao seu estado.</summary>
    /// <typeparam name="TIn">Tipo do valor do resultado.</typeparam>
    /// <typeparam name="TOut">Tipo produzido pelos ramos.</typeparam>
    /// <param name="result">Resultado a consumir.</param>
    /// <param name="onSuccess">Ramo executado em caso de sucesso, recebendo o valor.</param>
    /// <param name="onFailure">Ramo executado em caso de falha, recebendo o erro.</param>
    public static TOut Match<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return result.IsSuccess ? onSuccess(result.Value) : onFailure(result.Error);
    }

    /// <summary>Transforma o valor de um resultado de sucesso; propaga o erro em caso de falha.</summary>
    /// <typeparam name="TIn">Tipo do valor original.</typeparam>
    /// <typeparam name="TOut">Tipo do valor transformado.</typeparam>
    /// <param name="result">Resultado de origem.</param>
    /// <param name="map">Transformação aplicada ao valor em caso de sucesso.</param>
    public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(map);

        return result.IsSuccess ? Result.Success(map(result.Value)) : Result.Failure<TOut>(result.Error);
    }

    /// <summary>Encadeia uma operação que retorna <see cref="Result{TOut}"/>; propaga o erro em caso de falha.</summary>
    /// <typeparam name="TIn">Tipo do valor original.</typeparam>
    /// <typeparam name="TOut">Tipo do valor produzido pela operação encadeada.</typeparam>
    /// <param name="result">Resultado de origem.</param>
    /// <param name="bind">Operação executada com o valor em caso de sucesso.</param>
    public static Result<TOut> Bind<TIn, TOut>(this Result<TIn> result, Func<TIn, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bind);

        return result.IsSuccess ? bind(result.Value) : Result.Failure<TOut>(result.Error);
    }

    /// <summary>Encadeia uma operação sem valor de retorno; propaga o erro em caso de falha.</summary>
    /// <typeparam name="TIn">Tipo do valor original.</typeparam>
    /// <param name="result">Resultado de origem.</param>
    /// <param name="bind">Operação executada com o valor em caso de sucesso.</param>
    public static Result Bind<TIn>(this Result<TIn> result, Func<TIn, Result> bind)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bind);

        return result.IsSuccess ? bind(result.Value) : Result.Failure(result.Error);
    }

    /// <summary>Encadeia uma operação assíncrona que retorna <see cref="Result{TOut}"/>.</summary>
    /// <typeparam name="TIn">Tipo do valor original.</typeparam>
    /// <typeparam name="TOut">Tipo do valor produzido pela operação encadeada.</typeparam>
    /// <param name="result">Resultado de origem.</param>
    /// <param name="bindAsync">Operação assíncrona executada com o valor em caso de sucesso.</param>
    public static async Task<Result<TOut>> BindAsync<TIn, TOut>(this Result<TIn> result, Func<TIn, Task<Result<TOut>>> bindAsync)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bindAsync);

        return result.IsSuccess
            ? await bindAsync(result.Value).ConfigureAwait(false)
            : Result.Failure<TOut>(result.Error);
    }

    /// <summary>Transforma o valor de um resultado assíncrono; propaga o erro em caso de falha.</summary>
    /// <typeparam name="TIn">Tipo do valor original.</typeparam>
    /// <typeparam name="TOut">Tipo do valor transformado.</typeparam>
    /// <param name="resultTask">Tarefa que produz o resultado de origem.</param>
    /// <param name="map">Transformação aplicada ao valor em caso de sucesso.</param>
    public static async Task<Result<TOut>> MapAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(resultTask);

        var result = await resultTask.ConfigureAwait(false);
        return result.Map(map);
    }

    /// <summary>Encadeia uma operação assíncrona sobre um resultado assíncrono.</summary>
    /// <typeparam name="TIn">Tipo do valor original.</typeparam>
    /// <typeparam name="TOut">Tipo do valor produzido pela operação encadeada.</typeparam>
    /// <param name="resultTask">Tarefa que produz o resultado de origem.</param>
    /// <param name="bindAsync">Operação assíncrona executada com o valor em caso de sucesso.</param>
    public static async Task<Result<TOut>> BindAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, Task<Result<TOut>>> bindAsync)
    {
        ArgumentNullException.ThrowIfNull(resultTask);

        var result = await resultTask.ConfigureAwait(false);
        return await result.BindAsync(bindAsync).ConfigureAwait(false);
    }

    /// <summary>Consome um resultado assíncrono, invocando o ramo correspondente ao seu estado.</summary>
    /// <typeparam name="TIn">Tipo do valor do resultado.</typeparam>
    /// <typeparam name="TOut">Tipo produzido pelos ramos.</typeparam>
    /// <param name="resultTask">Tarefa que produz o resultado a consumir.</param>
    /// <param name="onSuccess">Ramo executado em caso de sucesso, recebendo o valor.</param>
    /// <param name="onFailure">Ramo executado em caso de falha, recebendo o erro.</param>
    public static async Task<TOut> MatchAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(resultTask);

        var result = await resultTask.ConfigureAwait(false);
        return result.Match(onSuccess, onFailure);
    }
}
