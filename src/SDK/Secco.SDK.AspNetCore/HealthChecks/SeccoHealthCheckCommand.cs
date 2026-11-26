using System.Globalization;
using System.Net;

namespace Secco.SDK.AspNetCore.HealthChecks;

/// <summary>
/// Verbo <c>healthcheck</c> dos binários da plataforma: consulta <c>/health/ready</c> do próprio processo
/// e sai com 0 (saudável) ou 1. Serve ao <c>HEALTHCHECK</c> da imagem, que não tem curl nem wget.
/// Roda antes de existir um host, por isso não usa <c>IHttpClientFactory</c> nem logger.
/// </summary>
public static class SeccoHealthCheckCommand
{
	/// <summary>Verbo que consulta a prontidão do próprio processo e sai.</summary>
	public const string Verb = "healthcheck";

	private const int DefaultPort = 8080;

	/// <summary>Indica se o processo foi chamado como <c>healthcheck</c> (primeiro argumento, exato).</summary>
	/// <param name="args">Argumentos do processo.</param>
	public static bool IsHealthCheck(string[] args)
	{
		ArgumentNullException.ThrowIfNull(args);
		return args.Length > 0 && string.Equals(args[0], Verb, StringComparison.Ordinal);
	}

	/// <summary>Consulta <c>/health/ready</c> na porta HTTP do processo, lida do ambiente, com timeout de 5 s.</summary>
	/// <param name="cancellationToken">Cancelamento.</param>
	/// <returns>0 se a resposta foi 200; 1 em qualquer outro caso.</returns>
	public static Task<int> RunAsync(CancellationToken cancellationToken = default)
	{
		var port = ResolvePort(
			Environment.GetEnvironmentVariable("ASPNETCORE_URLS"),
			Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"));

		if (port is null)
		{
			Console.Error.WriteLine("healthcheck: porta inválida na configuração do ambiente");
			return Task.FromResult(1);
		}

		return RunAsync(port.Value, handler: null, TimeSpan.FromSeconds(5), cancellationToken);
	}

	internal static int? ResolvePort(string? urls, string? httpPorts)
	{
		if (!string.IsNullOrWhiteSpace(urls))
		{
			foreach (var item in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				const string scheme = "http://";
				if (!item.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var authority = item[scheme.Length..];
				var slash = authority.IndexOf('/', StringComparison.Ordinal);
				if (slash >= 0)
				{
					authority = authority[..slash];
				}

				var colon = authority.LastIndexOf(':');
				return colon < 0 ? null : ParsePort(authority[(colon + 1)..]);
			}
		}

		if (!string.IsNullOrWhiteSpace(httpPorts))
		{
			var first = httpPorts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
			return first is null ? null : ParsePort(first);
		}

		return DefaultPort;
	}

	private static int? ParsePort(string text)
		=> int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535
			? port
			: null;

	internal static async Task<int> RunAsync(int port, HttpMessageHandler? handler, TimeSpan timeout, CancellationToken cancellationToken)
	{
		using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
		client.Timeout = timeout;

		using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/health/ready"));
		request.Headers.Host = "localhost";

#pragma warning disable CA1031 // Ponto de entrada de processo: qualquer falha vira exit code 1, nunca exceção
		try
		{
			using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
			if (response.StatusCode == HttpStatusCode.OK)
			{
				return 0;
			}

			Console.Error.WriteLine($"healthcheck: {(int)response.StatusCode}");
			return 1;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"healthcheck: {ex.GetType().Name}");
			return 1;
		}
#pragma warning restore CA1031
	}
}
