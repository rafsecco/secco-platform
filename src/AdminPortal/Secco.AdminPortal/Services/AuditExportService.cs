using System.Net.Http.Headers;
using Secco.AdminPortal.Authentication;
using Secco.LogStream.Client;
using Secco.SDK.RelyingParty;
using Secco.SharedKernel.Constants;

namespace Secco.AdminPortal.Services;

/// <summary>Filtros da exportação, já convertidos para instante.</summary>
/// <param name="From">A partir de.</param>
/// <param name="To">Até.</param>
/// <param name="ActorId">Ator exato.</param>
/// <param name="Action">Ação exata.</param>
public sealed record AuditExportFilter(DateTimeOffset? From, DateTimeOffset? To, string? ActorId, string? Action);

/// <summary>Por que a exportação não saiu — um código fixo, nunca o texto vindo do LogStream.</summary>
public enum AuditExportFailure
{
	/// <summary>Filtro inválido.</summary>
	InvalidRequest,

	/// <summary>Mais linhas que o limite do formato.</summary>
	TooManyRows,

	/// <summary>Sem permissão de exportar.</summary>
	Forbidden,

	/// <summary>Exportações demais seguidas.</summary>
	RateLimited,

	/// <summary>LogStream indisponível ou erro inesperado.</summary>
	Unavailable,
}

/// <summary>Falha da exportação, com o código para a tela.</summary>
/// <param name="failure">Código.</param>
public sealed class AuditExportException(AuditExportFailure failure) : Exception(failure.ToString())
{
	/// <summary>Código para a tela.</summary>
	public AuditExportFailure Failure { get; } = failure;
}

/// <summary>Arquivo baixado do LogStream; descartar libera a resposta HTTP de origem.</summary>
/// <param name="response">Resposta do client NSwag.</param>
public sealed class AuditExportFile(FileResponse response) : IAsyncDisposable
{
	/// <summary>Conteúdo.</summary>
	public Stream Content => response.Stream;

	/// <summary>Tamanho, quando a origem informa.</summary>
	public long? Length => response.Headers.TryGetValue("Content-Length", out var values)
		&& long.TryParse(values.FirstOrDefault(), out var length) ? length : null;

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		response.Dispose();
		return ValueTask.CompletedTask;
	}
}

/// <summary>Exportação da trilha de um tenant, on-behalf-of o operador (ADR-0058, ADR-0024).</summary>
public interface IAuditExportService
{
	/// <summary>Baixa a exportação do LogStream.</summary>
	/// <param name="tenantId">Tenant alvo (header X-Tenant-Id).</param>
	/// <param name="format"><c>xlsx</c> ou <c>pdf</c>.</param>
	/// <param name="filter">Filtros.</param>
	/// <param name="timeZone">Fuso IANA das datas.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <exception cref="AuditExportException">O LogStream recusou ou falhou.</exception>
	Task<AuditExportFile> ExportAsync(Guid tenantId, string format, AuditExportFilter filter, string timeZone, CancellationToken cancellationToken = default);
}

internal sealed class LogStreamAuditExportService(
	IHttpClientFactory httpClientFactory,
	ISeccoUserTokenProvider tokenProvider) : IAuditExportService
{
	public async Task<AuditExportFile> ExportAsync(
		Guid tenantId, string format, AuditExportFilter filter, string timeZone, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(filter);

		var http = httpClientFactory.CreateClient(AdminPortalDefaults.LogStreamExportHttpClient);

		if (await tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false) is { Length: > 0 } token)
		{
			http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}

		http.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, tenantId.ToString());

		try
		{
			// Texto preenchido nas falhas: é dele que sai o código TooManyRows (ver LogStreamQueryService)
			var response = await new LogStreamClient(http) { ReadResponseAsString = true }.ExportAuditEntriesAsync(
				format, filter.From, filter.To, filter.ActorId, filter.Action,
				resourceType: null, resourceId: null, correlationId: null, timeZone, cancellationToken).ConfigureAwait(false);

			return new AuditExportFile(response);
		}
		catch (ApiException exception)
		{
			throw new AuditExportException(exception.StatusCode switch
			{
				400 when exception.Response?.Contains("TooManyRows", StringComparison.Ordinal) == true => AuditExportFailure.TooManyRows,
				400 => AuditExportFailure.InvalidRequest,
				403 => AuditExportFailure.Forbidden,
				429 => AuditExportFailure.RateLimited,
				_ => AuditExportFailure.Unavailable,
			});
		}
		catch (HttpRequestException)
		{
			throw new AuditExportException(AuditExportFailure.Unavailable);
		}
	}
}
