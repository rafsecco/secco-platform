namespace Secco.SharedKernel.Results;

/// <summary>
/// Categoria semântica de um <see cref="Error"/>. Permite que camadas externas
/// (ex.: o Secco.SDK) mapeiem erros de negócio para protocolos específicos
/// (ex.: status HTTP / ProblemDetails) sem que o kernel conheça esses protocolos.
/// </summary>
public enum ErrorType
{
    /// <summary>Ausência de erro — reservado a <see cref="Error.None"/>.</summary>
    None = 0,

    /// <summary>Falha de negócio genérica, sem categoria mais específica.</summary>
    Failure = 1,

    /// <summary>Entrada inválida (equivalente HTTP: 400).</summary>
    Validation = 2,

    /// <summary>Recurso não encontrado (equivalente HTTP: 404).</summary>
    NotFound = 3,

    /// <summary>Conflito com o estado atual do recurso (equivalente HTTP: 409).</summary>
    Conflict = 4,

    /// <summary>Chamador não autenticado (equivalente HTTP: 401).</summary>
    Unauthorized = 5,

    /// <summary>Chamador autenticado sem permissão (equivalente HTTP: 403).</summary>
    Forbidden = 6,
}
