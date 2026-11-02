using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Secco.AdminPortal.Authentication;
using Secco.AdminPortal.Services;
using Secco.SDK.RelyingParty;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>
/// Exportação da trilha pelo console (ADR-0058): baixa do LogStream on-behalf-of o operador, no tenant
/// escolhido, e traduz a recusa em código fixo — nunca o texto vindo da origem. O download é POST
/// com antiforgery e exige o papel de operador.
/// </summary>
public class AuditExportTests(HealthCheckTests.AdminPortalFactory factory) : IClassFixture<HealthCheckTests.AdminPortalFactory>
{
	private sealed class Handler(HttpStatusCode status, string body, string contentType) : HttpMessageHandler
	{
		public HttpRequestMessage? Request { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Request = request;
			return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, contentType) });
		}
	}

	private static (LogStreamAuditExportService Service, Handler Handler) Build(HttpStatusCode status, string body, string contentType = "application/pdf")
	{
		var handler = new Handler(status, body, contentType);
		var services = new ServiceCollection();
		services.AddHttpClient(AdminPortalDefaults.LogStreamExportHttpClient, client => client.BaseAddress = new Uri("https://logstream.test"))
			.ConfigurePrimaryHttpMessageHandler(() => handler);

		var tokens = Substitute.For<ISeccoUserTokenProvider>();
		tokens.GetAccessTokenAsync().Returns("token-do-operador");

		return (new LogStreamAuditExportService(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), tokens), handler);
	}

	private static readonly Guid Tenant = Guid.Parse("018f0000-0000-7000-8000-0000000000aa");

	[Fact]
	public async Task Baixa_on_behalf_of_o_operador_no_tenant_escolhido()
	{
		var (service, handler) = Build(HttpStatusCode.OK, "%PDF-1.4 conteúdo");

		await using var file = await service.ExportAsync(
			Tenant, "pdf", new AuditExportFilter(null, null, "maria", "documento.download"), "America/Sao_Paulo");

		handler.Request!.Headers.Authorization!.ToString().Should().Be("Bearer token-do-operador");
		handler.Request.Headers.GetValues("X-Tenant-Id").Should().Equal(Tenant.ToString());
		var query = handler.Request.RequestUri!.Query;
		query.Should().Contain("format=pdf").And.Contain("actorId=maria").And.Contain("action=documento.download")
			.And.Contain("timeZone=America%2FSao_Paulo");
		handler.Request.Method.Should().Be(HttpMethod.Get);

		using var reader = new StreamReader(file.Content);
		(await reader.ReadToEndAsync()).Should().StartWith("%PDF-1.4");
	}

	[Theory]
	[InlineData(HttpStatusCode.BadRequest, """{"title":"Documents.Export.TooManyRows"}""", AuditExportFailure.TooManyRows)]
	[InlineData(HttpStatusCode.BadRequest, """{"title":"LogStream.AuditEntry.InvalidDateRange"}""", AuditExportFailure.InvalidRequest)]
	[InlineData(HttpStatusCode.Forbidden, "", AuditExportFailure.Forbidden)]
	[InlineData(HttpStatusCode.TooManyRequests, "", AuditExportFailure.RateLimited)]
	[InlineData(HttpStatusCode.ServiceUnavailable, "<script>origem</script>", AuditExportFailure.Unavailable)]
	public async Task Recusa_vira_codigo_fixo(HttpStatusCode status, string body, AuditExportFailure expected)
	{
		var (service, _) = Build(status, body, "application/problem+json");

		var act = () => service.ExportAsync(Tenant, "xlsx", new AuditExportFilter(null, null, null, null), "UTC");

		(await act.Should().ThrowAsync<AuditExportException>()).Which.Failure.Should().Be(expected);
	}

	[Fact]
	public void Download_e_so_post_com_antiforgery_e_policy_de_operador()
	{
		var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
			.OfType<RouteEndpoint>()
			.Single(e => e.RoutePattern.RawText == "/tenants/{tenantId:guid}/audit-entries/export");

		endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods.Should().Equal("POST");
		endpoint.Metadata.GetMetadata<IAntiforgeryMetadata>()!.RequiresValidation.Should().BeTrue();
		endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Should().Contain(AdminPortalDefaults.OperatorPolicy);
	}
}
