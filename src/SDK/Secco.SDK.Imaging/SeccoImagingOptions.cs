namespace Secco.SDK.Imaging;

/// <summary>Limites de defesa do processamento de imagens (ADR-0042).</summary>
public sealed class SeccoImagingOptions
{
	/// <summary>Maior entrada aceita, em bytes (padrão 10 MB).</summary>
	public long MaxInputBytes { get; set; } = 10L * 1024 * 1024;

	/// <summary>Maior área aceita (largura x altura), conferida no cabeçalho antes de decodificar (padrão 50 milhões).</summary>
	public long MaxPixels { get; set; } = 50_000_000;

	/// <summary>Quantas operações rodam ao mesmo tempo no processo (padrão: número de processadores).</summary>
	public int MaxConcurrentOperations { get; set; } = Environment.ProcessorCount;
}
