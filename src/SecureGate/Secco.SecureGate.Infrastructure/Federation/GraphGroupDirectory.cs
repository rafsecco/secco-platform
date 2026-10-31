using System.Net;
using System.Net.Http.Headers;
using System.Text;
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

		var token = await AcquireTokenAsync(directoryId, entra, cancellationToken).ConfigureAwait(false);

		if (token.IsFailure)
		{
			return Result.Failure<EntraGroupPage>(token.Error);
		}

		var accessToken = token.Value;

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

	/// <summary>Teto de páginas por leitura de membros (999 por página): nenhuma leitura prende o ciclo sem fim.</summary>
	private const int MaxMemberPages = 1000;

	/// <summary>Limite de grupos por chamada do <c>checkMemberGroups</c> do Graph.</summary>
	private const int CheckMemberGroupsBatchSize = 20;

	/// <summary>
	/// Quanto o login espera, no total, pela pertinência aos grupos (ADR-0055). Mais curto que a resiliência padrão
	/// do cliente HTTP: o login nunca fica refém do diretório; estourou, a leitura falha e o login segue como estava.
	/// </summary>
	internal TimeSpan CheckMemberGroupsTimeout { get; init; } = TimeSpan.FromSeconds(15);

	public async Task<Result<IReadOnlyList<Guid>>> GetGroupMemberIdsAsync(
		Guid directoryId, Guid groupId, CancellationToken cancellationToken = default)
	{
		// O id entra na URL por formatação de Guid — nunca texto livre (ADR-0020)
		if (directoryId == Guid.Empty || groupId == Guid.Empty)
		{
			return Result.Failure<IReadOnlyList<Guid>>(SecureGateErrors.Federation.DirectoryObjectNotFound);
		}

		var entra = options.Value;

		if (!entra.IsConfigured)
		{
			return Result.Failure<IReadOnlyList<Guid>>(SecureGateErrors.Federation.NotEnabled);
		}

		var token = await AcquireTokenAsync(directoryId, entra, cancellationToken).ConfigureAwait(false);

		if (token.IsFailure)
		{
			return Result.Failure<IReadOnlyList<Guid>>(token.Error);
		}

		var members = new HashSet<Guid>();
		string? url = $"{GraphBaseUrl}groups/{groupId:D}/transitiveMembers/microsoft.graph.user?$select=id&$top=999";

		for (var page = 0; url is not null; page++)
		{
			// A paginação não fechou dentro do teto: leitura incompleta, nunca sucesso parcial
			if (page >= MaxMemberPages)
			{
				LogDirectoryUnavailable(logger, directoryId, "paginação acima do teto");

				return Result.Failure<IReadOnlyList<Guid>>(SecureGateErrors.Federation.DirectoryReadIncomplete);
			}

			var read = await SendAsync(
				directoryId, token.Value, HttpMethod.Get, url, jsonBody: null, ParseMembersPage, cancellationToken)
				.ConfigureAwait(false);

			if (read.IsFailure)
			{
				return Result.Failure<IReadOnlyList<Guid>>(read.Error);
			}

			members.UnionWith(read.Value.Ids);
			url = read.Value.NextLink;
		}

		return Result.Success<IReadOnlyList<Guid>>([.. members]);
	}

	public async Task<Result<IReadOnlySet<Guid>>> CheckMemberGroupsAsync(
		Guid directoryId, Guid objectId, IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(groupIds);

		if (directoryId == Guid.Empty || objectId == Guid.Empty)
		{
			return Result.Failure<IReadOnlySet<Guid>>(SecureGateErrors.Federation.DirectoryObjectNotFound);
		}

		var wanted = groupIds.Where(id => id != Guid.Empty).Distinct().ToList();

		if (wanted.Count == 0)
		{
			return Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid>());
		}

		var entra = options.Value;

		if (!entra.IsConfigured)
		{
			return Result.Failure<IReadOnlySet<Guid>>(SecureGateErrors.Federation.NotEnabled);
		}

		var token = await AcquireTokenAsync(directoryId, entra, cancellationToken).ConfigureAwait(false);

		if (token.IsFailure)
		{
			return Result.Failure<IReadOnlySet<Guid>>(token.Error);
		}

		var memberOf = new HashSet<Guid>();
		var url = $"{GraphBaseUrl}users/{objectId:D}/checkMemberGroups";

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(CheckMemberGroupsTimeout);

		try
		{
			// Em lotes de 20: se qualquer lote falhar a resposta inteira falha — nada parcial
			foreach (var batch in wanted.Chunk(CheckMemberGroupsBatchSize))
			{
				var body = JsonSerializer.Serialize(new { groupIds = batch.Select(id => id.ToString("D")) });
				var requested = batch.ToHashSet();

				var read = await SendAsync(
					directoryId, token.Value, HttpMethod.Post, url, body, root => ParseCheckedGroups(root, requested), timeout.Token)
					.ConfigureAwait(false);

				if (read.IsFailure)
				{
					return Result.Failure<IReadOnlySet<Guid>>(read.Error);
				}

				memberOf.UnionWith(read.Value);
			}
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Estourou o limite do login (e não foi o chamador que cancelou): leitura falha, nada parcial
			LogDirectoryUnavailable(logger, directoryId, "tempo esgotado na pertinência a grupos");

			return Result.Failure<IReadOnlySet<Guid>>(SecureGateErrors.Federation.DirectoryUnavailable);
		}

		return Result.Success<IReadOnlySet<Guid>>(memberOf);
	}

	/// <summary>
	/// Token de aplicação do diretório, com a tradução de erro padrão: 400/401/403 na emissão = app nunca consentida
	/// naquele diretório; o resto = indisponível. Nunca devolve o corpo da resposta (ADR-0020).
	/// </summary>
	private async Task<Result<string>> AcquireTokenAsync(
		Guid directoryId, SecureGateEntraIdOptions entra, CancellationToken cancellationToken)
	{
		try
		{
			return await tokenCache
				.GetOrIssueAsync(directoryId, ct => IssueAppTokenAsync(directoryId, entra, ct), cancellationToken)
				.ConfigureAwait(false);
		}
		catch (GraphRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
		{
			LogConsentRequired(logger, directoryId, (int)ex.StatusCode);

			return Result.Failure<string>(SecureGateErrors.Federation.ConsentRequired);
		}
		catch (Exception ex) when (ex is GraphRequestException or HttpRequestException or TaskCanceledException)
		{
			LogDirectoryUnavailable(logger, directoryId, ex.Message);

			return Result.Failure<string>(SecureGateErrors.Federation.DirectoryUnavailable);
		}
	}

	/// <summary>
	/// Uma chamada ao Graph com a tradução de erro padrão (401/403 = sem consentimento, 404 = objeto inexistente,
	/// resto = indisponível). O <paramref name="parse"/> copia o que precisa: o documento JSON é descartado aqui.
	/// </summary>
	private async Task<Result<T>> SendAsync<T>(
		Guid directoryId,
		string accessToken,
		HttpMethod method,
		string url,
		string? jsonBody,
		Func<JsonElement, Result<T>> parse,
		CancellationToken cancellationToken)
	{
		try
		{
			var client = httpClientFactory.CreateClient(HttpClientName);
			using var request = new HttpRequestMessage(method, url);
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

			// Inofensivo sem $count/$search/$filter; é o que o Graph pede para consulta avançada de diretório
			request.Headers.TryAddWithoutValidation("ConsistencyLevel", "eventual");

			if (jsonBody is not null)
			{
				request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
			}

			using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

			if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
			{
				LogConsentRequired(logger, directoryId, (int)response.StatusCode);

				return Result.Failure<T>(SecureGateErrors.Federation.ConsentRequired);
			}

			if (response.StatusCode == HttpStatusCode.NotFound)
			{
				return Result.Failure<T>(SecureGateErrors.Federation.DirectoryObjectNotFound);
			}

			if (!response.IsSuccessStatusCode)
			{
				LogDirectoryUnavailable(logger, directoryId, $"HTTP {(int)response.StatusCode}");

				return Result.Failure<T>(SecureGateErrors.Federation.DirectoryUnavailable);
			}

			using var document = await JsonDocument.ParseAsync(
				await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return parse(document.RootElement);
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
			&& !cancellationToken.IsCancellationRequested)
		{
			LogDirectoryUnavailable(logger, directoryId, ex.GetType().Name);

			return Result.Failure<T>(SecureGateErrors.Federation.DirectoryUnavailable);
		}
	}

	private static Result<(List<Guid> Ids, string? NextLink)> ParseMembersPage(JsonElement root)
	{
		var incomplete = Result.Failure<(List<Guid> Ids, string? NextLink)>(SecureGateErrors.Federation.DirectoryReadIncomplete);

		if (root.ValueKind != JsonValueKind.Object
			|| !root.TryGetProperty("value", out var value)
			|| value.ValueKind != JsonValueKind.Array)
		{
			return incomplete;
		}

		var ids = new List<Guid>();

		foreach (var member in value.EnumerateArray())
		{
			if (member.ValueKind != JsonValueKind.Object
				|| !member.TryGetProperty("id", out var id)
				|| id.ValueKind != JsonValueKind.String
				|| !Guid.TryParseExact(id.GetString(), "D", out var parsed))
			{
				return incomplete;
			}

			ids.Add(parsed);
		}

		string? nextLink = null;

		if (root.TryGetProperty("@odata.nextLink", out var next) && next.ValueKind != JsonValueKind.Null)
		{
			// O cursor tem de ser uma URL do PRÓPRIO Graph: o token de aplicação não segue para outro lugar (SSRF)
			if (next.ValueKind != JsonValueKind.String
				|| next.GetString() is not { } link
				|| !link.StartsWith(GraphBaseUrl, StringComparison.Ordinal))
			{
				return incomplete;
			}

			nextLink = link;
		}

		return Result.Success((ids, nextLink));
	}

	private static Result<HashSet<Guid>> ParseCheckedGroups(JsonElement root, HashSet<Guid> requested)
	{
		if (root.ValueKind != JsonValueKind.Object
			|| !root.TryGetProperty("value", out var value)
			|| value.ValueKind != JsonValueKind.Array)
		{
			return Result.Failure<HashSet<Guid>>(SecureGateErrors.Federation.DirectoryReadIncomplete);
		}

		var memberOf = new HashSet<Guid>();

		foreach (var item in value.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.String || !Guid.TryParseExact(item.GetString(), "D", out var group))
			{
				return Result.Failure<HashSet<Guid>>(SecureGateErrors.Federation.DirectoryReadIncomplete);
			}

			// Só vale o que foi perguntado: um id a mais na resposta nunca vira grupo "justificado"
			if (requested.Contains(group))
			{
				memberOf.Add(group);
			}
		}

		return Result.Success(memberOf);
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
