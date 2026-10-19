using FluentAssertions;
using Secco.SecureGate.Application.Provisioning;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// A barreira de injeção desta entrega. Identificador não pode ser parametrizado em DDL, então
/// esta allowlist é a única coisa entre um nome vindo da requisição e um comando executado com
/// privilégio de administrador do servidor (ADR-0020).
/// </summary>
public class DatabaseIdentifierPolicyTests
{
	[Theory]
	[InlineData("secco_logstream_contoso")]
	[InlineData("abc")]
	[InlineData("a1_2_3")]
	public void IsValid_WhenNameMatchesAllowlist_ReturnsTrue(string identifier) =>
		DatabaseIdentifierPolicy.IsValid(identifier).Should().BeTrue();

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("ab")]                              // curto demais
	[InlineData("1abc")]                            // não começa por letra
	[InlineData("Contoso")]                         // maiúscula
	[InlineData("secco-logstream")]                 // hífen
	[InlineData("secco logstream")]                 // espaço
	[InlineData("secco;DROP DATABASE master--")]    // a tentativa óbvia
	[InlineData("secco']; EXEC sp_configure--")]    // aspa + fechamento
	[InlineData("secco]")]                          // fechamento de colchete
	[InlineData("secco--comentario")]
	[InlineData("secco/*bloco*/")]
	public void IsValid_WhenNameEscapesTheAllowlist_ReturnsFalse(string? identifier) =>
		DatabaseIdentifierPolicy.IsValid(identifier).Should().BeFalse();

	[Theory]
	[InlineData("master")]
	[InlineData("msdb")]
	[InlineData("tempdb")]
	[InlineData("postgres")]
	public void IsValid_WhenNameIsReserved_ReturnsFalse(string identifier) =>
		DatabaseIdentifierPolicy.IsValid(identifier).Should().BeFalse(
			"provisionar por cima de um banco de sistema é acidente grave e irreversível");

	[Fact]
	public void IsValid_WhenNameExceedsMaxLength_ReturnsFalse() =>
		DatabaseIdentifierPolicy.IsValid("a" + new string('b', DatabaseIdentifierPolicy.MaxLength))
			.Should().BeFalse();

	[Fact]
	public void Quote_WhenNameIsValid_DelimitsIt() =>
		DatabaseIdentifierPolicy.QuoteSqlServer("secco_logstream_contoso")
			.Should().Be("[secco_logstream_contoso]");

	[Fact]
	public void Quote_WhenNameWasNotValidated_Throws()
	{
		// Defesa em profundidade: um caminho novo que esqueça o IsValid falha aqui, não no banco.
		var act = () => DatabaseIdentifierPolicy.QuoteSqlServer("master]; DROP DATABASE x--");

		act.Should().Throw<ArgumentException>();
	}

	[Theory]
	[InlineData("Contoso S/A", "secco", "logstream", "secco_logstream_contoso_s_a")]
	[InlineData("acme", "secco", "logstream", "secco_logstream_acme")]
	public void Derive_NormalizesFreeTextIntoAValidIdentifier(
		string slug, string prefix, string product, string expected)
	{
		var derived = DatabaseIdentifierPolicy.Derive(prefix, product, slug);

		derived.Should().Be(expected);
		DatabaseIdentifierPolicy.IsValid(derived).Should().BeTrue();
	}

	[Fact]
	public void Derive_WhenResultWouldExceedMaxLength_TruncatesToAValidIdentifier()
	{
		var derived = DatabaseIdentifierPolicy.Derive("secco", "logstream", new string('x', 200));

		derived.Length.Should().BeLessThanOrEqualTo(DatabaseIdentifierPolicy.MaxLength);
		DatabaseIdentifierPolicy.IsValid(derived).Should().BeTrue();
	}

	[Fact]
	public void DeriveForTenant_NomeCurto_IgualAoDerive()
	{
		DatabaseIdentifierPolicy.DeriveForTenant(Guid.NewGuid(), "secco", "logstream", "compras")
			.Should().Be(DatabaseIdentifierPolicy.Derive("secco", "logstream", "compras"));
	}

	[Fact]
	public void DeriveForTenant_SlugsLongosComOMesmoComeco_NaoColidem()
	{
		// Truncados, os dois dariam o mesmo nome: o sufixo do id do tenant os separa (ADR-0047)
		var comum = new string('a', 45);
		var primeiro = DatabaseIdentifierPolicy.DeriveForTenant(Guid.NewGuid(), "secco", "notificationhub", comum + "-um");
		var segundo = DatabaseIdentifierPolicy.DeriveForTenant(Guid.NewGuid(), "secco", "notificationhub", comum + "-dois");

		primeiro.Should().NotBe(segundo);
		DatabaseIdentifierPolicy.IsValid(primeiro).Should().BeTrue();
		DatabaseIdentifierPolicy.IsValid(segundo).Should().BeTrue();
		primeiro.Length.Should().BeLessThanOrEqualTo(DatabaseIdentifierPolicy.MaxLength);
	}

	[Fact]
	public void DeriveForTenant_MesmoTenant_Deterministico()
	{
		var tenant = Guid.NewGuid();
		var slug = new string('b', 50);

		DatabaseIdentifierPolicy.DeriveForTenant(tenant, "secco", "notificationhub", slug)
			.Should().Be(DatabaseIdentifierPolicy.DeriveForTenant(tenant, "secco", "notificationhub", slug));
	}
}
