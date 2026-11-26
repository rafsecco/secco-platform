using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Secco.SecureGate.Api.Extensions;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Fora de Development e Testing o certificado de assinatura é obrigatório (ADR-0020): uma chave
/// gerada dentro do contêiner morreria com ele e invalidaria todo token emitido.
/// </summary>
public sealed class SigningCertificateSourceTests : IDisposable
{
	private const string Password = "senha-de-teste";

	private readonly List<string> _tempFiles = [];

	public void Dispose()
	{
		foreach (var file in _tempFiles)
		{
			File.Delete(file);
		}
	}

	private sealed class FakeEnvironment(string name) : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = name;
		public string ApplicationName { get; set; } = "Test";
		public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}

	private static byte[] NewPkcs12(string? password, bool withPrivateKey = true)
	{
		using var rsa = RSA.Create(2048);
		var request = new CertificateRequest("CN=secco-teste", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
		return withPrivateKey
			? certificate.Export(X509ContentType.Pkcs12, password)
			: certificate.Export(X509ContentType.Cert);
	}

	private string WriteTemp(byte[] bytes)
	{
		var path = Path.Combine(Path.GetTempPath(), $"secco-sign-{Guid.NewGuid():N}.pfx");
		File.WriteAllBytes(path, bytes);
		_tempFiles.Add(path);
		return path;
	}

	private static IConfiguration Config(params (string Key, string Value)[] items) =>
		new ConfigurationBuilder()
			.AddInMemoryCollection(items.ToDictionary(i => i.Key, i => (string?)i.Value))
			.Build();

	[Fact]
	public void Load_Development_Null() =>
		SigningCertificateSource.Load(Config(), new FakeEnvironment("Development")).Should().BeNull();

	[Fact]
	public void Load_Testing_Null() =>
		SigningCertificateSource.Load(Config(), new FakeEnvironment("Testing")).Should().BeNull();

	private void AssertLoadsFromPath(string environment)
	{
		var path = WriteTemp(NewPkcs12(Password));

		using var certificate = SigningCertificateSource.Load(
			Config(("SecureGate:Signing:CertificatePath", path), ("SecureGate:Signing:CertificatePassword", Password)),
			new FakeEnvironment(environment));

		certificate.Should().NotBeNull();
		certificate!.HasPrivateKey.Should().BeTrue();
	}

	[Fact]
	public void Load_ProductionComPath_Carrega() => AssertLoadsFromPath("Production");

	[Fact]
	public void Load_StagingComPath_Carrega() => AssertLoadsFromPath("Staging");

	[Fact]
	public void Load_ProductionComBase64_Carrega()
	{
		var base64 = Convert.ToBase64String(NewPkcs12(Password));

		using var certificate = SigningCertificateSource.Load(
			Config(("SecureGate:Signing:CertificateBase64", base64), ("SecureGate:Signing:CertificatePassword", Password)),
			new FakeEnvironment("Production"));

		certificate.Should().NotBeNull();
		certificate!.HasPrivateKey.Should().BeTrue();
	}

	[Fact]
	public void Load_Base64ComQuebrasDeLinha_Carrega()
	{
		var base64 = Convert.ToBase64String(NewPkcs12(Password), Base64FormattingOptions.None);
		var wrapped = string.Join('\n', Enumerable.Range(0, (base64.Length + 75) / 76)
			.Select(i => base64.Substring(i * 76, Math.Min(76, base64.Length - (i * 76)))));

		using var certificate = SigningCertificateSource.Load(
			Config(("SecureGate:Signing:CertificateBase64", wrapped), ("SecureGate:Signing:CertificatePassword", Password)),
			new FakeEnvironment("Staging"));

		certificate.Should().NotBeNull();
	}

	[Fact]
	public void Load_StagingSemNada_Lanca()
	{
		var act = () => SigningCertificateSource.Load(Config(), new FakeEnvironment("Staging"));

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*SecureGate:Signing:CertificatePath*");
	}

	[Fact]
	public void Load_PathEBase64_Lanca()
	{
		var bytes = NewPkcs12(Password);
		var path = WriteTemp(bytes);

		var act = () => SigningCertificateSource.Load(
			Config(
				("SecureGate:Signing:CertificatePath", path),
				("SecureGate:Signing:CertificateBase64", Convert.ToBase64String(bytes)),
				("SecureGate:Signing:CertificatePassword", Password)),
			new FakeEnvironment("Production"));

		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void Load_Base64Invalido_Lanca()
	{
		const string invalid = "isto-nao-e-base64-@@@";

		var act = () => SigningCertificateSource.Load(
			Config(("SecureGate:Signing:CertificateBase64", invalid)),
			new FakeEnvironment("Production"));

		var ex = act.Should().Throw<InvalidOperationException>().Which;
		ex.Message.Should().NotContain(invalid);
		ex.Message.Should().Contain("SecureGate:Signing:CertificateBase64");
		ex.InnerException.Should().NotBeNull();
	}

	[Fact]
	public void Load_SenhaErrada_Lanca()
	{
		var path = WriteTemp(NewPkcs12(Password));

		var act = () => SigningCertificateSource.Load(
			Config(("SecureGate:Signing:CertificatePath", path), ("SecureGate:Signing:CertificatePassword", "outra-senha")),
			new FakeEnvironment("Production"));

		var ex = act.Should().Throw<InvalidOperationException>().Which;
		ex.Message.Should().NotContain("outra-senha");
		ex.InnerException.Should().NotBeNull();
	}

	[Fact]
	public void Load_SemChavePrivada_Lanca()
	{
		var path = WriteTemp(NewPkcs12(null, withPrivateKey: false));

		var act = () => SigningCertificateSource.Load(
			Config(("SecureGate:Signing:CertificatePath", path)),
			new FakeEnvironment("Production"));

		act.Should().Throw<InvalidOperationException>();
	}
}
