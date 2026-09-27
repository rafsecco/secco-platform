using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Infrastructure.Federation;

/// <summary>
/// Lê grupos do diretório federado via Microsoft Graph, com <see cref="HttpClient"/> puro
/// (ADR-0036) — sem <c>Microsoft.Graph</c> nem <c>Microsoft.Identity.Client</c>.
/// </summary>
/// <remarks>
/// O token de aplicação vem do MESMO client id/secret da app registration do login federado
/// (ADR-0026, <see cref="SecureGateEntraIdOptions"/>), agora pedindo <c>.default</c> em vez do
/// consentimento delegado. Falha de emissão de token (a app nunca foi consentida naquele
/// diretório) e falha na própria chamada de grupos (permissão de aplicação sem consentimento de
/// admin) chegam por caminhos HTTP diferentes — as duas viram <see cref="SecureGateErrors.Federation.ConsentRequired"/>,
/// porque a ação corretiva é a mesma para o admin do cliente.
/// </remarks>
internal sealed partial class GraphGroupDirectory(
	IHttpClientFactory httpClientFactory,
	IOptions<SecureGateEntraIdOptions> options,
	GraphAppTokenCache tokenCache,
	ILogger<GraphGroupDirectory> logger) : IEntraGroupDirectory
{
	/// <summary>Nome do <see cref="HttpClient"/> nomeado usado para o token e para o Graph.</summary>
	public const string HttpClientName = "secco-securegate-entra-graph";

	private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0/";
	private const string GraphScope = "https://graph.microsoft.com/.default";

	public async Task<Result<EntraGroupPage>> ListGroupsAsync(
		Guid directoryId,
		string? nameFilter,
		string? pageToken,
		int pageSize,
		CancellationToken cancellationToken = default)
	{
		// Página seguinte é a URL que o PRÓPRIO Graph devolveu — nunca uma URL arbitrária vinda do
		// chamador. Sem essa checagem, o token de aplicação da plataforma faria requisição para
		// onde quer que o pageToken apontasse (SSRF, ADR-0020).
		if (pageToken is not null && !pageToken.StartsWith(GraphBaseUrl, StringComparison.Ordinal))
		{
			return Result.Failure<EntraGroupPage>(SecureGateErrors.Federation.InvalidPageToken);
		}

		var entra = options.Value;

		if (!entra.IsConfigured)
		{
			return Result.Failure<EntraGroupPage>(SecureGateErrors.Federation.NotEnabled);
		}

		string accessToken;

		try
		{
			accessToken = await tokenCache
				.GetOrIssueAsync(directoryId, ct => IssueAppTokenAsync(directoryId, entra, ct), cancellationToken)
				.ConfigureAwait(false);
		}
		catch (GraphRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
		{
			LogConsentRequired(logger, directoryId, (int)ex.StatusCode);

			return Result.Failure<EntraGroupPage>(SecureGateErrors.Federation.ConsentRequired);
		}
		catch (Exception ex) when (ex is GraphRequestException or HttpRequestException or TaskCanceledException)
		{
			LogDirectoryUnavailable(logger, directoryId, ex.Message);

			return Result.Failure<EntraGroupPage>(SecureGateErrors.Federation.DirectoryUnavailable);
		}

		var url = pageToken ?? BuildGroupsUrl(nameFilter, pageSize);

		try
		{
			var client = httpClientFactory.CreateClient(HttpClientName);
			using var request = new HttpRequestMessage(HttpMethod.Get, url);
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

			using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

			if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
			{
				LogConsentRequired(logger, directoryId, (int)response.StatusCode);

				return Result.Failure<EntraGroupPage>(SecureGateErrors.Federation.ConsentRequired);
			}

			if (!response.IsSuccessStatusCode)
			{
				LogDirectoryUnavailable(logger, directoryId, $"HTTP {(int)response.StatusCode}");

				return Result.Failure<EntraGroupPage>(SecureGateErrors.Federation.DirectoryUnavailable);
			}

			return ParsePage(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
		{
			LogDirectoryUnavailable(logger, directoryId, ex.Message);

			return Result.Failure<EntraGroupPage>(SecureGateErrors.Federation.DirectoryUnavailable);
		}
	}

	private static string BuildGroupsUrl(string? nameFilter, int pageSize)
	{
		var url = $"{GraphBaseUrl}groups?$select=id,displayName&$top={pageSize}";

		// O filtro já chega sanitizado (aspa dobrada, sem caractere de controle) por
		// EntraGroupNameFilter — esta camada só monta a URL, não valida de novo.
		if (!string.IsNullOrEmpty(nameFilter))
		{
			url += $"&$filter={Uri.EscapeDataString($"startswith(displayName,'{nameFilter}')")}";
		}

		return url;
	}

	private static EntraGroupPage ParsePage(Stream content)
	{
		using var document = JsonDocument.Parse(content);
		var root = document.RootElement;

		var items = new List<EntraGroupDto>();

		if (root.TryGetProperty("value", out var value))
		{
			foreach (var group in value.EnumerateArray())
			{
				var id = group.GetProperty("id").GetString();
				var displayName = group.TryGetProperty("displayName", out var name) ? name.GetString() : null;

				if (id is not null)
				{
					// Contagem de membros exige uma segunda chamada por grupo no Graph — fica de
					// fora da v1 por custo (a issue pede "se barato", e não é).
					items.Add(new EntraGroupDto(id, displayName ?? id, MemberCount: null));
				}
			}
		}

		var nextLink = root.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;

		return new EntraGroupPage(items, nextLink);
	}

	private async Task<(string AccessToken, TimeSpan ExpiresIn)> IssueAppTokenAsync(
		Guid directoryId, SecureGateEntraIdOptions entra, CancellationToken cancellationToken)
	{
		var client = httpClientFactory.CreateClient(HttpClientName);

		using var request = new HttpRequestMessage(
			HttpMethod.Post, $"https://login.microsoftonline.com/{directoryId:D}/oauth2/v2.0/token")
		{
			Content = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["grant_type"] = "client_credentials",
				["client_id"] = entra.ClientId!,
				["client_secret"] = entra.ClientSecret!,
				["scope"] = GraphScope,
			}),
		};

		using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			// Corpo da resposta nunca entra na mensagem (ADR-0020) — pode conter detalhe do
			// diretório do cliente.
			throw new GraphRequestException(response.StatusCode);
		}

		using var payload = JsonDocument.Parse(
			await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));

		var accessToken = payload.RootElement.GetProperty("access_token").GetString()
			?? throw new GraphRequestException(response.StatusCode);

		var expiresIn = payload.RootElement.TryGetProperty("expires_in", out var expiry) ? expiry.GetInt32() : 3600;

		return (accessToken, TimeSpan.FromSeconds(expiresIn));
	}

	[LoggerMessage(EventId = 3401, Level = LogLevel.Warning,
		Message = "Graph recusou por falta de consentimento de aplicação: diretório {DirectoryId}, HTTP {StatusCode}.")]
	private static partial void LogConsentRequired(ILogger logger, Guid directoryId, int statusCode);

	[LoggerMessage(EventId = 3402, Level = LogLevel.Warning,
		Message = "Microsoft Graph indisponível para o diretório {DirectoryId}: {Detail}.")]
	private static partial void LogDirectoryUnavailable(ILogger logger, Guid directoryId, string detail);

	/// <summary>Falha de emissão do token de aplicação — nunca carrega o corpo da resposta do Entra.</summary>
	private sealed class GraphRequestException(HttpStatusCode statusCode) : Exception
	{
		public HttpStatusCode StatusCode { get; } = statusCode;
	}
}
