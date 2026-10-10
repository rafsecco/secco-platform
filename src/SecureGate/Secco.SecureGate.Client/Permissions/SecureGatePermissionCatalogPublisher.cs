using Microsoft.Extensions.Logging;
using Secco.SDK.ClientCredentials;
using Secco.SecureGate.Client.Catalog;
using Secco.SharedKernel.Authorization;

namespace Secco.SecureGate.Client.Permissions;

/// <summary>
/// <see cref="ISecureGatePermissionCatalogPublisher"/> sobre o client gerado. O escopo pedido é
/// <c>permissions:&lt;produto&gt;</c> e só se conhece na chamada — por isso o pipeline de autenticação é
/// montado por publicação, sobre o handler do <c>HttpClient</c> nomeado (sem auth), e não no registro do
/// <c>IHttpClientFactory</c>. O token vive só durante a chamada (uma publicação por <c>migrate</c>).
/// </summary>
public sealed partial class SecureGatePermissionCatalogPublisher(
	SecureGateClientCredentialsOptions options,
	IHttpMessageHandlerFactory handlerFactory,
	ILogger<SecureGatePermissionCatalogPublisher> logger) : ISecureGatePermissionCatalogPublisher
{
	/// <summary>Nome do <c>HttpClient</c> nomeado (transporte, sem autenticação) usado pela publicação.</summary>
	public const string HttpClientName = "Secco.SecureGate.PermissionCatalog";

	/// <inheritdoc />
	public async Task PublishAsync(
		string product,
		IReadOnlyList<SeccoPermissionDefinition> permissions,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(product);
		ArgumentNullException.ThrowIfNull(permissions);

		if (!options.IsConfigured)
		{
			LogSkipped(logger, product);

			return;
		}

		options.Validate(requireProduct: false);

		using var authenticated = new SeccoClientCredentialsHandler(
			options.BaseUrl!, options.ClientId!, options.ClientSecret!,
			"permissions:" + product, new SeccoAccessTokenStore())
		{
			InnerHandler = handlerFactory.CreateHandler(HttpClientName),
		};
		using var http = new HttpClient(authenticated, disposeHandler: false)
		{
			BaseAddress = new Uri(options.BaseUrl!, UriKind.Absolute),
		};

		var client = new SecureGateClient(http);

		await client.PublishPermissionCatalogAsync(
			product,
			new PublishPermissionCatalogRequest
			{
				Permissions = [.. permissions.Select(p => new PermissionDefinitionRequest { Name = p.Name, Description = p.Description })],
			},
			cancellationToken).ConfigureAwait(false);

		LogPublished(logger, product, permissions.Count);
	}

	[LoggerMessage(
		EventId = 1,
		Level = LogLevel.Information,
		Message = "Catálogo de permissões do produto {Product} não publicado: seção 'Secco:SecureGate' ausente (ADR-0039).")]
	private static partial void LogSkipped(ILogger logger, string product);

	[LoggerMessage(
		EventId = 2,
		Level = LogLevel.Information,
		Message = "Catálogo de permissões do produto {Product} publicado no SecureGate ({Count} permissões).")]
	private static partial void LogPublished(ILogger logger, string product, int count);
}
