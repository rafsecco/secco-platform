namespace Secco.SDK.Logging.Internal;

/// <summary>
/// Guarda anti-recursão: categorias de log que o sink <b>nunca</b> envia ao LogStream.
/// </summary>
/// <remarks>
/// Isto é requisito de correção, não filtro de conveniência. O dispatcher envia os lotes por
/// <c>HttpClient</c>, e o <c>HttpClient</c> loga cada requisição em
/// <c>System.Net.Http.HttpClient.*</c>. Sem esta guarda, todo envio de lote produziria logs que
/// entrariam na fila, que provocariam outro envio, que produziria mais logs: um laço que enche a
/// fila sozinho e nunca converge — pior ainda quando o LogStream está fora do ar e a resiliência
/// começa a repetir tentativas.
/// <para>
/// O próprio pacote entra na lista para que os avisos do dispatcher (descarte, lote perdido)
/// cheguem aos providers locais — console, arquivo — e jamais tentem viajar até o LogStream.
/// </para>
/// </remarks>
internal static class LogStreamCategoryFilter
{
	private static readonly string[] IgnoredPrefixes =
	[
		"System.Net.Http",
		"Microsoft.Extensions.Http",
		"Polly",
		"Secco.SDK.Logging",
	];

	/// <summary>Indica se a categoria está fora do alcance do sink.</summary>
	/// <param name="category">Categoria do <c>ILogger</c>.</param>
	public static bool IsIgnored(string? category)
	{
		if (string.IsNullOrEmpty(category))
		{
			return true;
		}

		foreach (var prefix in IgnoredPrefixes)
		{
			if (category.StartsWith(prefix, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}
}
