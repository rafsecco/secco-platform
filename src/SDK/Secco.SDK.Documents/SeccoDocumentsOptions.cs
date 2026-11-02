using Microsoft.Extensions.Options;

namespace Secco.SDK.Documents;

/// <summary>Limites de defesa da exportação (ADR-0058).</summary>
public sealed class SeccoDocumentsOptions
{
	/// <summary>Maior número de linhas de uma planilha (padrão 100 mil; teto do Excel: 1.048.575 com o cabeçalho).</summary>
	public int MaxXlsxRows { get; set; } = 100_000;

	/// <summary>Maior número de linhas de um PDF (padrão 10 mil, perto de 300 páginas).</summary>
	public int MaxPdfRows { get; set; } = 10_000;

	/// <summary>Quantas exportações geram ao mesmo tempo no processo; as demais esperam vaga.</summary>
	public int MaxConcurrentExports { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

	/// <summary>Raiz do diretório temporário (padrão: o temporário do sistema). A SDK usa um subdiretório próprio.</summary>
	public string? TempDirectory { get; set; }
}

/// <summary>Valida os limites na subida (fail-fast, ADR-0020).</summary>
/// <param name="sectionKey">Chave da seção do produto, só para a mensagem de erro.</param>
public sealed class SeccoDocumentsOptionsValidator(string sectionKey) : IValidateOptions<SeccoDocumentsOptions>
{
	/// <summary>Maior número de linhas de dados que uma planilha do Excel comporta, fora o cabeçalho.</summary>
	public const int ExcelMaxDataRows = 1_048_575;

	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, SeccoDocumentsOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var failures = new List<string>();

		if (options.MaxXlsxRows is < 1 or > ExcelMaxDataRows)
		{
			failures.Add($"{sectionKey}:MaxXlsxRows deve estar entre 1 e {ExcelMaxDataRows}.");
		}

		if (options.MaxPdfRows < 1)
		{
			failures.Add($"{sectionKey}:MaxPdfRows deve ser maior que zero.");
		}

		if (options.MaxConcurrentExports < 1)
		{
			failures.Add($"{sectionKey}:MaxConcurrentExports deve ser maior que zero.");
		}

		return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
	}
}
