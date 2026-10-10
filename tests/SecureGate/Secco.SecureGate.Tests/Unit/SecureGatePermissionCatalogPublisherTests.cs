using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Client.Permissions;
using Secco.SharedKernel.Authorization;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Publicador do catálogo de permissões (ADR-0039): sem a seção <c>Secco:SecureGate</c> pula sem HTTP;
/// com a seção pede o token com <c>permissions:&lt;produto&gt;</c> e faz o PUT idempotente; falha lança.
/// </summary>
public class SecureGatePermissionCatalogPublisherTests
{
	private sealed class RecordingHandler(HttpStatusCode publishStatus = HttpStatusCode.NoContent) : HttpMessageHandler
	{
		public List<(HttpMethod Method, string Path, string? Authorization, string Body)> Calls { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
			Calls.Add((request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), body));

			if (request.RequestUri.AbsolutePath.EndsWith("connect/token", StringComparison.Ordinal))
			{
				return new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent("""{"access_token":"perm-token","expires_in":3600}""", Encoding.UTF8, "application/json"),
				};
			}

			return new HttpResponseMessage(publishStatus);
		}
	}

	private static readonly IReadOnlyDictionary<string, string?> Configured = new Dictionary<string, string?>
	{
		["Secco:SecureGate:BaseUrl"] = "https://securegate.test",
		["Secco:SecureGate:ClientId"] = "logstream-platform",
		["Secco:SecureGate:ClientSecret"] = "s3cret",
	};

	private static ServiceProvider BuildProvider(IReadOnlyDictionary<string, string?>? config, RecordingHandler handler)
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(config ?? new Dictionary<string, string?>()).Build();
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(configuration);
		services.AddLogging();
		services.AddSecureGatePermissionCatalogPublisher();
		services.AddHttpClient(SecureGatePermissionCatalogPublisher.HttpClientName)
			.ConfigurePrimaryHttpMessageHandler(() => handler);

		return services.BuildServiceProvider();
	}

	private static IReadOnlyList<SeccoPermissionDefinition> Catalog { get; } =
		[new("log-entries:read", "Ler logs"), new("log-entries:write", "Gravar logs")];

	[Fact]
	public async Task PublishAsync_WithoutSection_SkipsWithoutHttp()
	{
		var handler = new RecordingHandler();
		using var provider = BuildProvider(config: null, handler);

		await provider.GetRequiredService<ISecureGatePermissionCatalogPublisher>().PublishAsync("logstream", Catalog);

		handler.Calls.Should().BeEmpty();
	}

	[Fact]
	public async Task PublishAsync_WithSection_RequestsProductScopedTokenAndPutsTheCatalog()
	{
		var handler = new RecordingHandler();
		using var provider = BuildProvider(Configured, handler);

		await provider.GetRequiredService<ISecureGatePermissionCatalogPublisher>().PublishAsync("logstream", Catalog);

		handler.Calls.Should().HaveCount(2);
		handler.Calls[0].Path.Should().EndWith("connect/token");
		handler.Calls[0].Body.Should().Contain("scope=permissions%3Alogstream");
		handler.Calls[1].Method.Should().Be(HttpMethod.Put);
		handler.Calls[1].Path.Should().Be("/api/v1/permission-catalog/logstream");
		handler.Calls[1].Authorization.Should().Be("Bearer perm-token");
		handler.Calls[1].Body.Should().Contain("log-entries:read").And.Contain("Ler logs").And.Contain("log-entries:write");
	}

	[Theory]
	[InlineData(HttpStatusCode.Forbidden)]
	[InlineData(HttpStatusCode.InternalServerError)]
	public async Task PublishAsync_ServerRefuses_Throws(HttpStatusCode status)
	{
		using var provider = BuildProvider(Configured, new RecordingHandler(status));

		var act = async () => await provider.GetRequiredService<ISecureGatePermissionCatalogPublisher>()
			.PublishAsync("logstream", Catalog);

		await act.Should().ThrowAsync<Exception>();
	}

	[Fact]
	public async Task PublishAsync_PartialSection_FailsFast()
	{
		var partial = new Dictionary<string, string?> { ["Secco:SecureGate:BaseUrl"] = "https://securegate.test" };
		using var provider = BuildProvider(partial, new RecordingHandler());

		var act = async () => await provider.GetRequiredService<ISecureGatePermissionCatalogPublisher>()
			.PublishAsync("logstream", Catalog);

		await act.Should().ThrowAsync<InvalidOperationException>();
	}
}
