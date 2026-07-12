using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Secco.SharedKernel.Results;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>
/// Conversão padrão de <see cref="Result"/>/<see cref="Result{T}"/> para HTTP (ADR-0004):
/// sucesso delega ao caso de uso da borda; falha vira ProblemDetails com o status derivado
/// do <see cref="ErrorType"/>. <see cref="ValidationError"/> expande os erros individuais
/// na extensão <c>errors</c>; <see cref="ErrorType.Unavailable"/> responde 503 com <c>Retry-After</c>.
/// </summary>
public static class SeccoResultHttpExtensions
{
    /// <summary>Segundos sugeridos no <c>Retry-After</c> de respostas 503.</summary>
    public const int RetryAfterSeconds = 5;

    /// <summary>Converte o resultado em resposta HTTP, delegando o corpo de sucesso ao chamador.</summary>
    /// <typeparam name="T">Tipo do valor do resultado.</typeparam>
    /// <param name="result">Resultado do caso de uso.</param>
    /// <param name="onSuccess">Constrói a resposta de sucesso a partir do valor.</param>
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess ? onSuccess(result.Value) : ToProblem(result.Error);
    }

    /// <summary>Converte o resultado sem valor em resposta HTTP.</summary>
    /// <param name="result">Resultado do caso de uso.</param>
    /// <param name="onSuccess">Constrói a resposta de sucesso.</param>
    public static IResult ToHttpResult(this Result result, Func<IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess ? onSuccess() : ToProblem(result.Error);
    }

    private static IResult ToProblem(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = error.Code,
            Detail = error.Description,
        };

        if (error is ValidationError validationError)
        {
            problem.Extensions["errors"] = validationError.Errors
                .Select(e => new { code = e.Code, description = e.Description })
                .ToArray();
        }

        return error.Type == ErrorType.Unavailable
            ? new RetryableProblemResult(problem)
            : Results.Problem(problem);
    }

    /// <summary>503 com <c>Retry-After</c> — o chamador sabe que pode repetir em instantes.</summary>
    private sealed class RetryableProblemResult(ProblemDetails problem) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = problem.Status!.Value;
            httpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

            return httpContext.Response.WriteAsJsonAsync(
                problem,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: httpContext.RequestAborted);
        }
    }
}
