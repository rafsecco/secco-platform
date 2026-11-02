using Secco.SharedKernel.Results;

namespace Secco.SDK.Documents;

/// <summary>Erros de negócio da exportação. Descrições fixas, sem ecoar dado da listagem (ADR-0020).</summary>
public static class SeccoDocumentsErrors
{
	/// <summary>A listagem passa do limite de linhas do formato.</summary>
	public static readonly Error TooManyRows =
		Error.Validation("Documents.Export.TooManyRows", "A exportação passa do limite de linhas. Restrinja o filtro e tente de novo.");
}
