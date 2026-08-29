using FluentAssertions;
using Secco.SDK.Testing;
using Xunit;

namespace Secco.SDK.Testing.Tests;

/// <summary>
/// Modo de operação, allowlist de nome de database e isolamento por sufixo — tudo sem Docker:
/// o container só é construído no <c>StartAsync</c>, que estes testes nunca chamam (ADR-0027).
/// </summary>
public class SeccoSqlServerInstanceTests
{
	private const string ExternalConnectionString =
		"Server=localhost,1433;User Id=sa;Password=irrelevante;TrustServerCertificate=true";

	[Fact]
	public void Constructor_WithoutExternalConnectionString_UsesOwnContainer()
	{
		var instance = new SeccoSqlServerInstance(externalConnectionString: null);

		instance.UsesExternalInstance.Should().BeFalse();
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void Constructor_WithBlankExternalConnectionString_UsesOwnContainer(string value)
	{
		var instance = new SeccoSqlServerInstance(value);

		instance.UsesExternalInstance.Should().BeFalse();
	}

	[Fact]
	public void Constructor_WithExternalConnectionString_UsesExternalInstance()
	{
		var instance = new SeccoSqlServerInstance(ExternalConnectionString);

		instance.UsesExternalInstance.Should().BeTrue();
	}

	[Fact]
	public void GetServerConnectionString_BeforeStart_OnContainerMode_Throws()
	{
		var instance = new SeccoSqlServerInstance(externalConnectionString: null);

		var act = instance.GetServerConnectionString;

		act.Should().Throw<InvalidOperationException>().WithMessage("*StartAsync*");
	}

	[Theory]
	[InlineData("secco_logstream_alfa")]
	[InlineData("a")]
	[InlineData("secco_1")]
	public void ResolveDatabaseName_WithAllowedName_AppendsSuffix(string databaseName)
	{
		var instance = new SeccoSqlServerInstance(ExternalConnectionString);

		var resolved = instance.ResolveDatabaseName(databaseName);

		resolved.Should().Be($"{databaseName}_{instance.Suffix}");
	}

	[Theory]
	[InlineData("Secco_LogStream")]      // maiúsculas
	[InlineData("1secco")]               // começa com dígito
	[InlineData("secco-logstream")]      // hífen
	[InlineData("secco logstream")]      // espaço
	[InlineData("secco];DROP DATABASE[")] // tentativa de injeção
	[InlineData("secco_ç")]              // fora do ASCII
	public void ResolveDatabaseName_WithNameOutsideAllowlist_Throws(string databaseName)
	{
		var instance = new SeccoSqlServerInstance(ExternalConnectionString);

		var act = () => instance.ResolveDatabaseName(databaseName);

		act.Should().Throw<ArgumentException>().WithMessage("*inválido*");
	}

	[Fact]
	public void ResolveDatabaseName_WithNameAboveLimit_Throws()
	{
		var instance = new SeccoSqlServerInstance(ExternalConnectionString);

		var act = () => instance.ResolveDatabaseName("a" + new string('b', 91));

		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void Suffix_AcrossInstances_IsUnique()
	{
		var first = new SeccoSqlServerInstance(ExternalConnectionString);
		var second = new SeccoSqlServerInstance(ExternalConnectionString);

		// A propriedade que sustenta rodar duas suítes contra a MESMA instância externa:
		// mesmo nome-base, databases diferentes.
		second.Suffix.Should().NotBe(first.Suffix);
		second.ResolveDatabaseName("secco_alfa").Should().NotBe(first.ResolveDatabaseName("secco_alfa"));
	}

	[Fact]
	public void GetConnectionStringFor_OnExternalInstance_PointsToSuffixedDatabase()
	{
		var instance = new SeccoSqlServerInstance(ExternalConnectionString);

		var connectionString = instance.GetConnectionStringFor("secco_alfa");

		connectionString.Should().Contain($"secco_alfa_{instance.Suffix}");
	}
}
