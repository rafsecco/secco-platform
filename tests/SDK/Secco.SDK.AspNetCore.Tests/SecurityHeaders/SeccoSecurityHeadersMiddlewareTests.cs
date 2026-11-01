using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.SecurityHeaders;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.SecurityHeaders;

public sealed class SeccoSecurityHeadersMiddlewareTests
{
	private const string Csp = "Content-Security-Policy";

	private static async Task<IHost> StartAsync(
		Action<SeccoSecurityHeadersOptions>? configure = null,
		Action<IEndpointRouteBuilder>? endpoints = null,
		string environment = "Production",
		bool outputCache = false,
		bool exceptionHandler = false)
	{
		return await new HostBuilder()
			.UseEnvironment(environment)
			.ConfigureWebHost(web =>
			{
				web.UseTestServer();
				web.ConfigureServices(services =>
				{
					services.AddRouting();
					services.AddSeccoSecurityHeaders(configure);

					if (outputCache)
					{
						services.AddOutputCache();
					}
				});
				web.Configure(app =>
				{
					if (exceptionHandler)
					{
						app.UseExceptionHandler("/erro");
					}

					app.UseSeccoSecurityHeaders();
					app.UseRouting();

					if (outputCache)
					{
						app.UseOutputCache();
					}

					app.UseEndpoints(e =>
					{
						e.MapGet("/", () => "ok");
						e.MapGet("/nonce", (HttpContext context) => context.GetSeccoCspNonce());
						endpoints?.Invoke(e);
					});
				});
			})
			.StartAsync();
	}

	private static async Task<HttpResponseMessage> GetAsync(IHost host, string path, string? hostHeader = null)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, path);

		if (hostHeader is not null)
		{
			request.Headers.Host = hostHeader;
		}

		return await host.GetTestClient().SendAsync(request);
	}

	private static string Header(HttpResponseMessage response, string name)
		=> response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : string.Empty;

	private static string NonceFromCsp(HttpResponseMessage response)
	{
		var csp = Header(response, Csp);
		var start = csp.IndexOf("'nonce-", StringComparison.Ordinal) + "'nonce-".Length;
		return csp[start..csp.IndexOf('\'', start)];
	}

	[Fact]
	public async Task Sem_configuracao_sai_com_os_padroes_seguros()
	{
		using var host = await StartAsync();

		var response = await GetAsync(host, "/");

		Header(response, "X-Content-Type-Options").Should().Be("nosniff");
		Header(response, "Referrer-Policy").Should().Be("strict-origin-when-cross-origin");
		Header(response, "X-Frame-Options").Should().Be("SAMEORIGIN");
		Header(response, "Permissions-Policy").Should().Contain("camera=()").And.Contain("microphone=()").And.Contain("geolocation=()");

		var csp = Header(response, Csp);
		csp.Should().StartWith("default-src 'self'; script-src 'self' 'nonce-");
		csp.Should().Contain("style-src 'self' 'nonce-");
		csp.Should().Contain("style-src-attr 'none'");
		csp.Should().Contain("img-src 'self' data:");
		csp.Should().Contain("object-src 'none'");
		csp.Should().Contain("base-uri 'self'");
		csp.Should().Contain("frame-ancestors 'self'");
		csp.Should().NotContain("unsafe-inline").And.NotContain("unsafe-eval").And.NotContain("form-action");
		response.Headers.CacheControl.Should().BeNull();
	}

	[Fact]
	public async Task Nonce_e_novo_por_requisicao_e_o_mesmo_no_corpo_e_no_cabecalho()
	{
		using var host = await StartAsync();

		var first = await GetAsync(host, "/nonce");
		var second = await GetAsync(host, "/nonce");

		var firstNonce = await first.Content.ReadAsStringAsync();
		firstNonce.Should().Be(NonceFromCsp(first));
		firstNonce.Should().MatchRegex("^[A-Za-z0-9_-]{22}$", "base64url de 128 bits, sem caractere que o HTML codifique");
		Base64Url.DecodeFromChars(firstNonce).Should().HaveCount(16);
		(await second.Content.ReadAsStringAsync()).Should().NotBe(firstNonce);
	}

	[Fact]
	public async Task Resposta_que_usa_o_nonce_nao_vai_para_cache()
	{
		using var host = await StartAsync(endpoints: e => e.MapGet("/publica", (HttpContext context) =>
		{
			context.Response.Headers.CacheControl = "public, max-age=600";
			return context.GetSeccoCspNonce();
		}));

		var response = await GetAsync(host, "/publica");

		response.Headers.CacheControl!.NoStore.Should().BeTrue();
		response.Headers.CacheControl.Public.Should().BeFalse();
	}

	[Fact]
	public async Task Resposta_que_nao_usa_o_nonce_mantem_o_cache_do_produto()
	{
		using var host = await StartAsync(endpoints: e => e.MapGet("/publica", (HttpContext context) =>
		{
			context.Response.Headers.CacheControl = "public, max-age=600";
			return "ok";
		}));

		var response = await GetAsync(host, "/publica");

		response.Headers.CacheControl!.Public.Should().BeTrue();
	}

	[Fact]
	public async Task Output_cache_nao_guarda_resposta_com_nonce()
	{
		using var host = await StartAsync(
			endpoints: e =>
			{
				e.MapGet("/com-nonce", (HttpContext context) => context.GetSeccoCspNonce()).CacheOutput();
				e.MapGet("/sem-nonce", () => Guid.NewGuid().ToString()).CacheOutput();
			},
			outputCache: true);

		// Controle: o cache funciona neste host.
		var cachedA = await (await GetAsync(host, "/sem-nonce")).Content.ReadAsStringAsync();
		var cachedB = await (await GetAsync(host, "/sem-nonce")).Content.ReadAsStringAsync();
		cachedB.Should().Be(cachedA);

		var first = await (await GetAsync(host, "/com-nonce")).Content.ReadAsStringAsync();
		var second = await GetAsync(host, "/com-nonce");

		(await second.Content.ReadAsStringAsync()).Should().NotBe(first).And.Be(NonceFromCsp(second));
	}

	[Fact]
	public async Task Pagina_de_erro_reexecutada_mantem_cabecalhos_e_o_mesmo_nonce()
	{
		using var host = await StartAsync(
			endpoints: e =>
			{
				e.MapGet("/quebra", (HttpContext context) =>
				{
					_ = context.GetSeccoCspNonce();
					throw new InvalidOperationException("falha de teste");
				});
				e.MapGet("/erro", (HttpContext context) => context.GetSeccoCspNonce());
			},
			exceptionHandler: true);

		var response = await GetAsync(host, "/quebra");

		response.StatusCode.Should().Be(System.Net.HttpStatusCode.InternalServerError);
		Header(response, "X-Content-Type-Options").Should().Be("nosniff");
		(await response.Content.ReadAsStringAsync()).Should().Be(NonceFromCsp(response));
	}

	[Fact]
	public async Task DenyFraming_proibe_moldura_e_sobrescreve_o_x_frame_options_do_antiforgery()
	{
		using var host = await StartAsync(
			o => o.DenyFraming(),
			e => e.MapGet("/form", (HttpContext context) =>
			{
				// O antiforgery põe SAMEORIGIN por conta própria nas páginas com token.
				context.Response.Headers.XFrameOptions = "SAMEORIGIN";
				return "form";
			}));

		var response = await GetAsync(host, "/form");

		Header(response, Csp).Should().Contain("frame-ancestors 'none'");
		Header(response, "X-Frame-Options").Should().Be("DENY");
	}

	[Fact]
	public async Task Moldura_por_outra_origem_omite_x_frame_options()
	{
		using var host = await StartAsync(o =>
		{
			o.FrameAncestors.Clear();
			o.FrameAncestors.Add("https://portal.exemplo.com");
		});

		var response = await GetAsync(host, "/");

		Header(response, Csp).Should().Contain("frame-ancestors https://portal.exemplo.com");
		response.Headers.Contains("X-Frame-Options").Should().BeFalse();
	}

	[Fact]
	public async Task Origens_extras_e_form_action_entram_nas_diretivas_certas()
	{
		using var host = await StartAsync(o =>
		{
			o.ScriptSources.Add("https://cdn.exemplo.com");
			o.StyleSources.Add("https://fontes.exemplo.com");
			o.FontSources.Add("data:");
			o.ConnectSources.Add("wss://tempo-real.exemplo.com");
			o.FormActions.Add("'self'");
			o.FormActions.Add("https://login.exemplo.com");
		});

		var csp = Header(await GetAsync(host, "/"), Csp);

		csp.Should().MatchRegex(@"script-src 'self' 'nonce-[^']+' https://cdn\.exemplo\.com;");
		csp.Should().MatchRegex(@"style-src 'self' 'nonce-[^']+' https://fontes\.exemplo\.com;");
		csp.Should().Contain("font-src 'self' data:");
		csp.Should().Contain("connect-src 'self' wss://tempo-real.exemplo.com");
		csp.Should().Contain("form-action 'self' https://login.exemplo.com");
	}

	[Fact]
	public async Task Atributo_de_estilo_liberado_so_pelo_hash_do_valor_exato()
	{
		using var host = await StartAsync(o => o.AllowStyleAttribute("--sc-setor-hue:152").AllowStyleAttribute("--sc-setor-hue:152"));

		var csp = Header(await GetAsync(host, "/"), Csp);

		var expected = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("--sc-setor-hue:152")));
		csp.Should().Contain($"style-src-attr 'unsafe-hashes' 'sha256-{expected}';");
		csp.Should().NotContain("unsafe-inline");
	}

	[Fact]
	public async Task ReportOnly_troca_o_nome_do_cabecalho_e_aponta_o_coletor()
	{
		using var host = await StartAsync(o =>
		{
			o.Mode = SeccoCspMode.ReportOnly;
			o.ReportUri = new Uri("https://relatorios.exemplo.com/csp");
		});

		var response = await GetAsync(host, "/");

		response.Headers.Contains(Csp).Should().BeFalse();
		Header(response, "Content-Security-Policy-Report-Only").Should().EndWith("; report-uri https://relatorios.exemplo.com/csp");
		Header(response, "X-Frame-Options").Should().Be("SAMEORIGIN", "frame-ancestors não vale em Report-Only");
	}

	[Fact]
	public async Task Permissao_liberada_vale_para_a_propria_origem()
	{
		using var host = await StartAsync(o => o.AllowedPermissions.Add("camera"));

		var policy = Header(await GetAsync(host, "/"), "Permissions-Policy");

		policy.Should().Contain("camera=(self)").And.NotContain("camera=()").And.Contain("microphone=()");
	}

	[Fact]
	public async Task Hsts_sai_fora_de_development_mesmo_por_http()
	{
		using var host = await StartAsync();

		var response = await GetAsync(host, "/", "intranet.exemplo.com");

		response.RequestMessage!.RequestUri!.Scheme.Should().Be("http");
		Header(response, "Strict-Transport-Security").Should().Be("max-age=31536000");
	}

	[Fact]
	public async Task Hsts_com_subdominios_e_preload_quando_pedidos()
	{
		using var host = await StartAsync(o =>
		{
			o.HstsMaxAge = TimeSpan.FromDays(730);
			o.HstsIncludeSubDomains = true;
			o.HstsPreload = true;
		});

		var response = await GetAsync(host, "/", "intranet.exemplo.com");

		Header(response, "Strict-Transport-Security").Should().Be("max-age=63072000; includeSubDomains; preload");
	}

	[Theory]
	[InlineData("Development", "intranet.exemplo.com")]
	[InlineData("Production", "localhost")]
	[InlineData("Production", "127.0.0.1")]
	[InlineData("Production", "[::1]")]
	public async Task Hsts_nunca_sai_em_development_nem_em_loopback(string environment, string hostHeader)
	{
		using var host = await StartAsync(environment: environment);

		var response = await GetAsync(host, "/", hostHeader);

		response.Headers.Contains("Strict-Transport-Security").Should().BeFalse();
	}

	[Fact]
	public async Task Hsts_desligado_pela_opcao()
	{
		using var host = await StartAsync(o => o.HstsEnabled = false);

		var response = await GetAsync(host, "/", "intranet.exemplo.com");

		response.Headers.Contains("Strict-Transport-Security").Should().BeFalse();
	}

	[Fact]
	public async Task Servico_injetavel_entrega_o_nonce_do_cabecalho()
	{
		using var host = await StartAsync(endpoints: e => e.MapGet("/servico", (ISeccoCspNonce nonce) => nonce.Value));

		var response = await GetAsync(host, "/servico");

		(await response.Content.ReadAsStringAsync()).Should().Be(NonceFromCsp(response));
		response.Headers.CacheControl!.NoStore.Should().BeTrue();
	}

	[Fact]
	public void Nonce_sem_o_middleware_e_erro_do_programador()
	{
		var act = () => new DefaultHttpContext().GetSeccoCspNonce();

		act.Should().Throw<InvalidOperationException>().WithMessage("*UseSeccoSecurityHeaders*");
	}

	[Fact]
	public async Task Politica_invalida_derruba_a_subida()
	{
		var act = () => StartAsync(o => o.ScriptSources.Add("'unsafe-inline'"));

		await act.Should().ThrowAsync<OptionsValidationException>();
	}
}
