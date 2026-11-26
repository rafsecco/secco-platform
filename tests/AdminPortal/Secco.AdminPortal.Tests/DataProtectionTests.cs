using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.AdminPortal.Services;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>
/// As chaves do DataProtection (cookie e antiforgery) precisam sobreviver à recriação do contêiner:
/// com <c>AdminPortal:DataProtection:KeysPath</c> definido elas vivem na pasta, não na memória.
/// </summary>
public sealed class DataProtectionTests : IDisposable
{
	private readonly string _pasta = Path.Combine(Path.GetTempPath(), "secco-dp-" + Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		if (Directory.Exists(_pasta))
		{
			Directory.Delete(_pasta, recursive: true);
		}
	}

	private IDataProtectionProvider CriarProvider()
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["AdminPortal:DataProtection:KeysPath"] = _pasta,
			})
			.Build();

		var services = new ServiceCollection();
		services.AddLogging();
		services.AddAdminPortalDataProtection(configuration);
		return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
	}

	[Fact]
	public void KeysPath_Definido_GravaChaveNaPasta()
	{
		CriarProvider().CreateProtector("t").Protect("x");

		Directory.GetFiles(_pasta, "key-*.xml").Should().NotBeEmpty();
	}

	[Fact]
	public void KeysPath_Definido_ReabreComAMesmaChave()
	{
		var payload = CriarProvider().CreateProtector("t").Protect("x");

		CriarProvider().CreateProtector("t").Unprotect(payload).Should().Be("x");
	}
}
