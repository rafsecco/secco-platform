using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Secco.AdminPortal.Authentication;
using Secco.AdminPortal.Services;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>
/// A <see cref="SecureGateClientFactory"/> anexa o access token do operador ao client do
/// SecureGate (on-behalf-of, ADR-0023) — a custódia central do token para toda a gestão.
/// </summary>
public class SecureGateClientFactoryTests
{
	private sealed class CapturingHandler : HttpMessageHandler
	{
		public string? AuthorizationHeader { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			AuthorizationHeader = request.Headers.Authorization?.ToString();

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("[]", Encoding.UTF8, "application/json"),
			});
		}
	}

	private static (SecureGateClientFactory Factory, CapturingHandler Handler) Build(string? token)
	{
		var handler = new CapturingHandler();

		var services = new ServiceCollection();
		services.AddHttpClient(AdminPortalDefaults.SecureGateHttpClient, client =>
				client.BaseAddress = new Uri("https://securegate.test"))
			.ConfigurePrimaryHttpMessageHandler(() => handler);

		var httpClientFactory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

		var tokenProvider = Substitute.For<IOperatorTokenProvider>();
		tokenProvider.GetAccessTokenAsync().Returns(token);

		return (new SecureGateClientFactory(httpClientFactory, tokenProvider), handler);
	}

	[Fact]
	public async Task CreateAsync_WithToken_AttachesBearer()
	{
		var (factory, handler) = Build("operator-token");

		var client = await factory.CreateAsync();
		await client.ListTenantsAsync(CancellationToken.None);

		handler.AuthorizationHeader.Should().Be("Bearer operator-token");
	}

	[Fact]
	public async Task CreateAsync_WithoutToken_DoesNotAttachAuthorization()
	{
		var (factory, handler) = Build(token: null);

		var client = await factory.CreateAsync();
		await client.ListTenantsAsync(CancellationToken.None);

		handler.AuthorizationHeader.Should().BeNull();
	}

	private sealed class ConflictHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
			{
				Content = new StringContent(
					"""{"title":"Conflict","status":409,"detail":"Você não pode alterar o próprio vínculo."}""",
					Encoding.UTF8,
					"application/problem+json"),
			});
	}

	[Fact]
	public async Task CreateAsync_RecusaDocumentada_ChegaATelaComOMotivo()
	{
		// Sem ReadResponseAsString o NSwag deixa ApiException.Response nulo nas respostas tipadas
		// (ProblemDetails), e a tela mostrava só "HTTP 409".
		var services = new ServiceCollection();
		services.AddHttpClient(AdminPortalDefaults.SecureGateHttpClient, client =>
				client.BaseAddress = new Uri("https://securegate.test"))
			.ConfigurePrimaryHttpMessageHandler(() => new ConflictHandler());

		var tokenProvider = Substitute.For<IOperatorTokenProvider>();
		var factory = new SecureGateClientFactory(
			services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), tokenProvider);

		var client = await factory.CreateAsync();
		var act = () => client.SuspendTenantMemberAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

		var thrown = await act.Should().ThrowAsync<Secco.SecureGate.Client.ApiException>();
		ApiErrorFormatter.Describe(thrown.Which.StatusCode, thrown.Which.Response)
			.Should().Be("Você não pode alterar o próprio vínculo.");
	}
}
