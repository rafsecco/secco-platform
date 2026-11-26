using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Secco.SDK.AspNetCore.Extensions;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.ReverseProxy;

public class SeccoReverseProxyTests
{
	private const string Rede = "172.30.0.0/24";

	private static IHostBuilder CreateHost(Dictionary<string, string?> config, string? remoteIp)
	{
		return new HostBuilder().ConfigureWebHost(webBuilder =>
		{
			webBuilder.UseTestServer();
			webBuilder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(config));
			webBuilder.ConfigureServices(services => services.AddSeccoForwardedHeaders());
			webBuilder.Configure(app =>
			{
				app.Use((context, next) =>
				{
					if (remoteIp is not null)
					{
						context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
					}

					return next();
				});
				app.UseSeccoForwardedHeaders();
				app.Run(context => context.Response.WriteAsync(
					$"{context.Request.Scheme}|{context.Connection.RemoteIpAddress}"));
			});
		});
	}

	private static Dictionary<string, string?> Ligado(string rede = Rede) => new()
	{
		["Secco:ReverseProxy:Enabled"] = "true",
		["Secco:ReverseProxy:KnownNetworks:0"] = rede,
	};

	private static async Task<string> GetAsync(IHost host)
	{
		using var client = host.GetTestClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, "/scheme");
		request.Headers.Add("X-Forwarded-Proto", "https");
		request.Headers.Add("X-Forwarded-For", "203.0.113.9");
		return await client.SendAsync(request).ContinueWith(t => t.Result.Content.ReadAsStringAsync()).Unwrap();
	}

	[Fact]
	public async Task Desligado_IgnoraXForwardedProto()
	{
		using var host = await CreateHost([], "172.30.0.5").StartAsync();

		(await GetAsync(host)).Should().StartWith("http|");
	}

	[Fact]
	public async Task Ligado_OrigemNaRede_AplicaProtoEIp()
	{
		using var host = await CreateHost(Ligado(), "172.30.0.5").StartAsync();

		(await GetAsync(host)).Should().Be("https|203.0.113.9");
	}

	[Fact]
	public async Task Ligado_OrigemIpv4MapeadaEmIpv6_Aplica()
	{
		using var host = await CreateHost(Ligado(), "::ffff:172.30.0.5").StartAsync();

		(await GetAsync(host)).Should().StartWith("https|");
	}

	[Fact]
	public async Task Ligado_OrigemForaDaRede_Ignora()
	{
		using var host = await CreateHost(Ligado(), "10.0.0.5").StartAsync();

		(await GetAsync(host)).Should().StartWith("http|");
	}

	[Fact]
	public async Task Ligado_SemRede_NaoSobe()
	{
		var config = new Dictionary<string, string?> { ["Secco:ReverseProxy:Enabled"] = "true" };
		using var host = CreateHost(config, null).Build();

		var act = () => host.StartAsync();

		(await act.Should().ThrowAsync<OptionsValidationException>())
			.WithMessage("*Secco:ReverseProxy:KnownNetworks é obrigatória quando Enabled=true (ADR-0062).*");
	}

	[Fact]
	public async Task Ligado_CidrInvalido_NaoSobe()
	{
		using var host = CreateHost(Ligado("300.1.1.1/8"), null).Build();

		var act = () => host.StartAsync();

		(await act.Should().ThrowAsync<OptionsValidationException>())
			.WithMessage("*Secco:ReverseProxy:KnownNetworks:0 não é um CIDR válido.*");
	}

	[Fact]
	public async Task AddSeccoPlatform_RegistraForwardedHeaders()
	{
		var config = Ligado();
		config["Secco:Authentication:Audience"] = "secco-tests";
		config["Secco:Authentication:Issuer"] = "secco-tests";
		config["Secco:Authentication:DevelopmentSigningKey"] = "chave-de-testes-com-32-caracteres!!";

		using var host = await new HostBuilder().ConfigureWebHost(webBuilder =>
		{
			webBuilder.UseTestServer();
			webBuilder.UseEnvironment(Environments.Development);
			webBuilder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(config));
			webBuilder.ConfigureServices(services =>
			{
				services.AddRouting();
				services.AddSeccoPlatform();
			});
			webBuilder.Configure(app => { });
		}).StartAsync();

		var options = host.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

		options.KnownIPNetworks.Should().ContainSingle();
	}
}
