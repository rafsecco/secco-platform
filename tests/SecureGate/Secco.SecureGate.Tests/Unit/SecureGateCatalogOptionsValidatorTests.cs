using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Secco.SDK.EntityFrameworkCore.Cryptography;
using Secco.SecureGate.Infrastructure.Cryptography;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Fail-fast da chave de cifragem do catálogo no startup (ADR-0025/0020): 32 bytes exigidos,
/// Production sem chave ou com a chave de desenvolvimento embutida falha, e fora de Production
/// a ausência de chave é permitida (fallback para a chave embutida).
/// </summary>
public class SecureGateCatalogOptionsValidatorTests
{
	private static string ValidKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

	private static SecureGateCatalogOptionsValidator CreateValidator(string environment) =>
		new(new FakeHostEnvironment(environment));

	private static ValidateOptionsResult Validate(string environment, SecureGateCatalogOptions options) =>
		CreateValidator(environment).Validate(name: null, options);

	[Fact]
	public void Validate_ProductionWithValidKey_Succeeds()
	{
		var result = Validate("Production", new SecureGateCatalogOptions { EncryptionKey = ValidKey() });

		result.Succeeded.Should().BeTrue();
	}

	[Fact]
	public void Validate_ProductionWithoutKey_Fails()
	{
		var result = Validate("Production", new SecureGateCatalogOptions());

		result.Failed.Should().BeTrue();
		result.FailureMessage.Should().Contain("obrigatória em Production");
	}

	[Fact]
	public void Validate_ProductionWithEmbeddedDevelopmentKey_Fails()
	{
		var result = Validate("Production", new SecureGateCatalogOptions
		{
			EncryptionKey = SecureGateCatalogOptions.DevelopmentEncryptionKey,
		});

		result.Failed.Should().BeTrue();
		result.FailureMessage.Should().Contain("proibida em Production");
	}

	[Fact]
	public void Validate_DevelopmentWithoutKey_Succeeds()
	{
		// Fora de Production, a ausência de chave é aceita — o cipher cai na chave embutida
		var result = Validate("Development", new SecureGateCatalogOptions());

		result.Succeeded.Should().BeTrue();
	}

	[Theory]
	[InlineData(16)]
	[InlineData(31)]
	[InlineData(48)]
	public void Validate_KeyOfWrongByteLength_Fails(int keySizeInBytes)
	{
		var wrongSizeKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(keySizeInBytes));

		var result = Validate("Development", new SecureGateCatalogOptions { EncryptionKey = wrongSizeKey });

		result.Failed.Should().BeTrue();
		result.FailureMessage.Should().Contain("32 bytes");
	}

	[Fact]
	public void Validate_KeyThatIsNotBase64_Fails()
	{
		var result = Validate("Development", new SecureGateCatalogOptions { EncryptionKey = "não é base64!!!" });

		result.Failed.Should().BeTrue();
	}

	[Fact]
	public void Validate_RetiredKeyOfWrongLength_Fails()
	{
		var result = Validate("Development", new SecureGateCatalogOptions
		{
			EncryptionKey = ValidKey(),
			RetiredEncryptionKeys = [Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))],
		});

		result.Failed.Should().BeTrue();
		result.FailureMessage.Should().Contain("RetiredEncryptionKeys");
	}

	[Fact]
	public void DevelopmentFallback_EmbeddedKey_IsUsableByThePlatformCipher()
	{
		// A chave de DEV embutida precisa ser base64 de 32 bytes que o cifrador aceite — se
		// deixar de ser, todo ambiente sem chave configurada quebra no startup.
		//
		// Que o FALLBACK de fato acontece está provado por caminho mais forte que este: as
		// suítes de integração do SecureGate sobem SEM chave configurada e ainda assim
		// asseveram `secco-enc:v1:` direto na coluna. A política em si mudou de lugar na
		// ADR-0029 — saiu do construtor do cifrador e foi para a composição do produto, que
		// é onde política de produto pertence.
		var cipher = AesGcmSecretCipher.FromBase64Keys(SecureGateCatalogOptions.DevelopmentEncryptionKey);

		var encrypted = cipher.Encrypt("Server=dev;Database=x;");

		encrypted.Should().StartWith("secco-enc:v1:");
		cipher.Decrypt(encrypted).Should().Be("Server=dev;Database=x;");
	}

	private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = environmentName;

		public string ApplicationName { get; set; } = "Secco.Tests";

		public string ContentRootPath { get; set; } = string.Empty;

		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}
}
