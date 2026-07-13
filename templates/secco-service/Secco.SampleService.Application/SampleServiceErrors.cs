using Secco.SharedKernel.Results;

namespace Secco.SampleService.Application;

/// <summary>Erros de negócio do produto (ADR-0004): códigos estáveis <c>SampleService.*</c>.</summary>
public static class SampleServiceErrors
{
    /// <summary>Erros do recurso Sample (exemplo do template).</summary>
    public static class Samples
    {
        /// <summary>Nome ausente ou vazio.</summary>
        public static readonly Error NameRequired =
            Error.Validation("SampleService.Sample.NameRequired", "O nome é obrigatório.");

        /// <summary>Nome acima do limite configurado.</summary>
        public static Error NameTooLong(int limit) =>
            Error.Validation("SampleService.Sample.NameTooLong", $"O nome excede o limite de {limit} caracteres.");

        /// <summary>Registro não encontrado no banco do tenant atual.</summary>
        public static readonly Error NotFound =
            Error.NotFound("SampleService.Sample.NotFound", "Sample não encontrado.");
    }
}
