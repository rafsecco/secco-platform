using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Secco.SDK.AspNetCore.Ambient;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Ambient;

/// <summary>
/// O contexto ambiente existe para consumidores singleton (o sink de log). Estes testes provam
/// as duas propriedades das quais ele depende: os middlewares o populam, e um valor de uma
/// requisição não vaza para outra concorrente.
/// </summary>
public class SeccoAmbientContextTests : IAsyncLifetime
{
	private IHost _host = null!;
	private HttpClient _client = null!;

	public async Task InitializeAsync()
	{
		_host = await new HostBuilder()
			.ConfigureWebHost(webBuilder =>
			{
				webBuilder.UseTestServer();
				webBuilder.ConfigureServices(services =>
				{
					services.AddSeccoCorrelation();
					services.AddSeccoTenancy();
				});
				webBuilder.Configure(app =>
				{
					app.UseSeccoCorrelation();
					app.UseSeccoTenancy();
					app.Run(async context =>
					{
						// Um atraso força as requisições concorrentes a se sobreporem: se o
						// AsyncLocal vazasse entre elas, é aqui que apareceria.
						await Task.Delay(25);

						await context.Response.WriteAsync(
							$"{SeccoAmbientContext.TenantId}|{SeccoAmbientContext.CorrelationId}");
					});
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();
	}

	public async Task DisposeAsync()
	{
		_client.Dispose();
		await _host.StopAsync();
		_host.Dispose();
	}

	[Fact]
	public async Task Request_WithTenantHeader_ExposesTenantAndCorrelationAmbiently()
	{
		var tenantId = Guid.CreateVersion7();
		var correlationId = Guid.CreateVersion7();

		using var request = new HttpRequestMessage(HttpMethod.Get, "/");
		request.Headers.Add(SeccoHeaders.TenantId, tenantId.ToString());
		request.Headers.Add(SeccoHeaders.CorrelationId, correlationId.ToString());

		var body = await (await _client.SendAsync(request)).Content.ReadAsStringAsync();

		body.Should().Be($"{tenantId}|{correlationId}");
	}

	[Fact]
	public async Task Request_WithoutTenant_LeavesAmbientTenantUnresolved()
	{
		var body = await _client.GetStringAsync("/");

		body.Should().StartWith("|");
	}

	[Fact]
	public async Task ConcurrentRequests_OfDifferentTenants_DoNotLeakAmbientState()
	{
		var tenants = Enumerable.Range(0, 12).Select(_ => Guid.CreateVersion7()).ToList();

		var responses = await Task.WhenAll(tenants.Select(async tenantId =>
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, "/");
			request.Headers.Add(SeccoHeaders.TenantId, tenantId.ToString());

			var body = await (await _client.SendAsync(request)).Content.ReadAsStringAsync();

			return (Expected: tenantId, Body: body);
		}));

		responses.Should().OnlyContain(result => result.Body.StartsWith(result.Expected.ToString(), StringComparison.Ordinal));
	}

	[Fact]
	public void SetTenant_OutsideHttpPipeline_PopulatesAmbientTenant()
	{
		// O caminho dos jobs (ADR-0015): sem HttpContext, quem espelha o tenant é o SetTenant.
		var services = new ServiceCollection();
		services.AddSeccoTenancy();

		using var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();

		var tenantId = Guid.CreateVersion7();
		scope.ServiceProvider.SetTenant(tenantId);

		SeccoAmbientContext.TenantId.Should().Be(tenantId);
	}
}
