using Microsoft.Extensions.Options;

namespace Secco.SDK.Documents.Tests;

/// <summary>Linha de exemplo com todos os tipos de coluna.</summary>
internal sealed record Linha(string? Texto, long? Inteiro, decimal? Numero, decimal? Valor, DateOnly? Data, DateTimeOffset? Instante);

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
	public DateTimeOffset Now { get; set; } = now;

	public override DateTimeOffset GetUtcNow() => Now;
}

internal static class ExportFixture
{
	internal static readonly DateTimeOffset Agora = new(2026, 11, 1, 15, 30, 0, TimeSpan.Zero);

	internal static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

	internal static ExportColumns<Linha> Colunas() => new ExportColumns<Linha>()
		.Text("Texto", l => l.Texto, 40)
		.WholeNumber("Inteiro", l => l.Inteiro)
		.Number("Número", l => l.Numero)
		.Currency("Valor", l => l.Valor)
		.Date("Data", l => l.Data)
		.DateTime("Instante", l => l.Instante);

	internal static ExportRequest<Linha> Pedido(ExportFormat format, IEnumerable<Linha> linhas) =>
		new(format, "Trilha de auditoria", "trilha-de-auditoria", Colunas(), ToAsync(linhas))
		{
			TenantName = "Acme Indústria",
			Filters = [new ExportFilter("Ator", "maria@acme.test"), new ExportFilter("Período", "01/10/2026 a 31/10/2026")],
			IssuedBy = "Maria Operadora",
			TimeZone = SaoPaulo,
		};

	internal static Linha Exemplo(int i) =>
		new($"linha {i}", i, i + 0.5m, 1234.56m, new DateOnly(2026, 10, 1), new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

	internal static IEnumerable<Linha> Muitas(int count) => Enumerable.Range(1, count).Select(Exemplo);

	internal static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
	{
		foreach (var item in items)
		{
			await Task.Yield();
			yield return item;
		}
	}

	internal static SeccoDocumentExporter Exporter(string tempRoot, Action<SeccoDocumentsOptions>? configure = null, TimeProvider? time = null)
	{
		var options = new SeccoDocumentsOptions { TempDirectory = tempRoot };
		configure?.Invoke(options);
		return new SeccoDocumentExporter(Options.Create(options), time ?? new FixedTime(Agora));
	}

	internal static async Task<byte[]> ExportBytesAsync(SeccoDocumentExporter exporter, ExportRequest<Linha> request)
	{
		var result = await exporter.ExportAsync(request);

		if (result.IsFailure)
		{
			throw new InvalidOperationException(result.Error.Code);
		}

		await using var document = result.Value;
		using var copy = new MemoryStream();
		await document.Content.CopyToAsync(copy);
		return copy.ToArray();
	}
}

/// <summary>Diretório temporário isolado por teste.</summary>
public abstract class TempRootTest : IDisposable
{
	protected TempRootTest()
	{
		Root = Path.Combine(Path.GetTempPath(), "secco-documents-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Root);
	}

	protected string Root { get; }

	protected string StoreDirectory => Path.Combine(Root, Internal.TempFileStore.DirectoryName);

	public void Dispose()
	{
		GC.SuppressFinalize(this);
		try
		{
			Directory.Delete(Root, recursive: true);
		}
		catch (IOException)
		{
			// Melhor esforço: o temporário do sistema limpa o resto.
		}
	}
}
