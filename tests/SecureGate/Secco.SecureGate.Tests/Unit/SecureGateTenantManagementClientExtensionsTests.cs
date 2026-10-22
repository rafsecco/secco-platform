using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Client;
using Secco.SecureGate.Client.Administration;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Composição do client de gestão do próprio tenant (ADR-0052,
/// <see cref="SecureGateAdminClientExtensions.AddSecureGateTenantManagementClient"/>): pede
/// <c>securegate:tenant-manage</c> com a credencial da subseção — nunca a do client de plataforma da seção pai — e
/// configuração parcial falha no primeiro uso em vez de virar client sem autenticação.
/// </summary>
public class SecureGateTenantManagementClientExtensionsTests
{
	private const string BaseUrl = "https://securegate.test";

	/// <summary>Responde ao <c>connect/token</c> capturando o formulário, e às demais chamadas capturando o Bearer.</summary>
	private sealed class TokenIssuingHandler : HttpMessageHandler
	{
		public string? TokenRequestForm { get; private set; }

		public string? CapturedAuthorizationHeader { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri!.AbsolutePath.EndsWith("connect/token", StringComparison.Ordinal))
			{
				TokenRequestForm = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync(cancellationToken));

				return new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent(
						"""{"access_token":"tenant-manage-token","expires_in":300}""", Encoding.UTF8, "application/json"),
				};
			}

			CapturedAuthorizationHeader = request.Headers.Authorization?.ToString();

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("[]", Encoding.UTF8, "application/json"),
			};
		}
	}

	private static ServiceProvider BuildProvider(IReadOnlyDictionary<string, string?> configuration, HttpMessageHandler primaryHandler)
	{
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
		services.AddLogging();
		services.AddSecureGateTenantManagementClient();
		services.AddHttpClient<ISecureGateClient, SecureGateClient>()
			.ConfigurePrimaryHttpMessageHandler(() => primaryHandler);

		return services.BuildServiceProvider();
	}

	[Fact]
	public async Task ComSubsecaoCompleta_PedeTenantManage_ComACredencialDaSubsecao()
	{
		var handler = new TokenIssuingHandler();
		using var provider = BuildProvider(new Dictionary<string, string?>
		{
			["Secco:SecureGate:BaseUrl"] = BaseUrl,
			["Secco:SecureGate:ClientId"] = "intranet-plataforma",
			["Secco:SecureGate:ClientSecret"] = "segredo-da-plataforma",
			["Secco:SecureGate:TenantManagement:ClientId"] = "cli_intranet0000000",
			["Secco:SecureGate:TenantManagement:ClientSecret"] = "segredo-do-client-de-produto",
		}, handler);

		await provider.GetRequiredService<ISecureGateClient>().ListRolesAsync(Guid.NewGuid(), CancellationToken.None);

		handler.CapturedAuthorizationHeader.Should().Be("Bearer tenant-manage-token");
		handler.TokenRequestForm.Should().Contain("scope=securegate:tenant-manage")
			.And.Contain("cli_intranet0000000")
			.And.NotContain("securegate:admin")
			.And.NotContain("intranet-plataforma", "a credencial de plataforma nunca pede a gestão do tenant");
	}

	[Fact]
	public void SecaoPaiCompletaSemSubsecao_FalhaNoPrimeiroUso()
	{
		using var provider = BuildProvider(new Dictionary<string, string?>
		{
			["Secco:SecureGate:BaseUrl"] = BaseUrl,
			["Secco:SecureGate:ClientId"] = "intranet-plataforma",
			["Secco:SecureGate:ClientSecret"] = "segredo-da-plataforma",
		}, new TokenIssuingHandler());

		var act = () => provider.GetRequiredService<ISecureGateClient>();

		act.Should().Throw<InvalidOperationException>().WithMessage("*TenantManagement:ClientId*");
	}

	[Fact]
	public async Task SemNadaConfigurado_FalhaSemBaseAddress()
	{
		using var provider = BuildProvider(new Dictionary<string, string?>(), new TokenIssuingHandler());
		var client = provider.GetRequiredService<ISecureGateClient>();

		var act = async () => await client.ListRolesAsync(Guid.NewGuid(), CancellationToken.None);

		await act.Should().ThrowAsync<InvalidOperationException>();
	}
}
