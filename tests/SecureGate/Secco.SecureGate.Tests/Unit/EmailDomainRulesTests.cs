using FluentAssertions;
using Secco.SecureGate.Domain.Tenants;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Domínios de e-mail permitidos da federação (ADR-0055): input do operador que depois decide quem vira conta.
/// Formato estrito, sem curinga, com teto de quantidade e de tamanho.
/// </summary>
public class EmailDomainRulesTests
{
	[Theory]
	[InlineData("empresa.com.br", "empresa.com.br")]
	[InlineData("  Empresa.COM  ", "empresa.com")]
	[InlineData("sub-dominio.empresa.io", "sub-dominio.empresa.io")]
	[InlineData("xn--exemplo-9ua.com", "xn--exemplo-9ua.com")]
	public void TryNormalize_DominioValido_NormalizaEmMinusculas(string input, string expected)
	{
		EmailDomainRules.TryNormalize(input, out var domain).Should().BeTrue();
		domain.Should().Be(expected);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("semponto")]
	[InlineData("*.empresa.com")]
	[InlineData("*")]
	[InlineData("@empresa.com")]
	[InlineData("a@empresa.com")]
	[InlineData("empresa..com")]
	[InlineData(".empresa.com")]
	[InlineData("empresa.com.")]
	[InlineData("-empresa.com")]
	[InlineData("empresa-.com")]
	[InlineData("em presa.com")]
	[InlineData("empresa_x.com")]
	[InlineData("empresa.com/path")]
	[InlineData("empresa.com,outra.com")]
	[InlineData("192.168.0.1")]
	[InlineData("empresa.123")]
	[InlineData("empresá.com")]
	[InlineData("empresa.com\r\nX-Injecao: 1")]
	public void TryNormalize_DominioInvalido_Recusa(string? input)
	{
		EmailDomainRules.TryNormalize(input, out _).Should().BeFalse();
	}

	[Fact]
	public void TryNormalize_AcimaDoLimiteDoDns_Recusa()
	{
		var label = new string('a', 63);
		var tooLong = $"{label}.{label}.{label}.{label}.com";

		tooLong.Length.Should().BeGreaterThan(EmailDomainRules.MaxDomainLength);
		EmailDomainRules.TryNormalize(tooLong, out _).Should().BeFalse();
		EmailDomainRules.TryNormalize($"{new string('a', 64)}.com", out _).Should().BeFalse("rótulo acima de 63");
	}

	[Fact]
	public void TryNormalizeAll_RemoveRepetidosEOrdena()
	{
		EmailDomainRules.TryNormalizeAll(["b.com", "A.com", "b.COM"], out var domains).Should().BeTrue();

		domains.Should().Equal("a.com", "b.com");
	}

	[Fact]
	public void TryNormalizeAll_ListaVaziaEValida_EsvaziaOProvisionamento()
	{
		EmailDomainRules.TryNormalizeAll([], out var domains).Should().BeTrue();

		domains.Should().BeEmpty();
	}

	[Fact]
	public void TryNormalizeAll_UmItemInvalido_RecusaALista()
	{
		EmailDomainRules.TryNormalizeAll(["ok.com", "*.ruim.com"], out _).Should().BeFalse();
	}

	[Fact]
	public void TryNormalizeAll_AcimaDoTeto_Recusa()
	{
		var eleven = Enumerable.Range(0, EmailDomainRules.MaxCount + 1).Select(i => $"d{i}.com").ToList();
		var ten = eleven.Take(EmailDomainRules.MaxCount);

		EmailDomainRules.TryNormalizeAll(eleven, out _).Should().BeFalse();
		EmailDomainRules.TryNormalizeAll(ten, out _).Should().BeTrue();
	}

	[Fact]
	public void ListaNoTamanhoMaximo_CabeNaColuna()
	{
		// O pior caso: o teto de domínios, cada um no teto de tamanho, mais os separadores
		var worst = Enumerable.Repeat(new string('a', EmailDomainRules.MaxDomainLength), EmailDomainRules.MaxCount);

		EmailDomainRules.Serialize(worst).Length.Should().Be(EmailDomainRules.ColumnMaxLength);
	}

	[Fact]
	public void SerializeEDeserialize_FazemIdaEVolta()
	{
		var stored = EmailDomainRules.Serialize(["a.com", "b.com"]);

		EmailDomainRules.Deserialize(stored).Should().Equal("a.com", "b.com");
		EmailDomainRules.Deserialize(null).Should().BeEmpty();
		EmailDomainRules.Deserialize(string.Empty).Should().BeEmpty();
	}

	[Fact]
	public void Deserialize_ItemMalformadoNoBanco_EDescartado_NuncaAdivinhado()
	{
		EmailDomainRules.Deserialize("ok.com,*.curinga.com,,outro.com").Should().Equal("ok.com", "outro.com");
	}

	[Fact]
	public void Federacao_NasceSemDominios_EGuardaOsNormalizados()
	{
		var federation = new TenantFederation(Guid.NewGuid(), Guid.NewGuid());

		federation.GetAllowedEmailDomains().Should().BeEmpty("sem lista o provisionamento recusa todo e-mail");

		federation.SetAllowedEmailDomains(["Empresa.com", "empresa.com.br"]);

		federation.GetAllowedEmailDomains().Should().Equal("empresa.com", "empresa.com.br");
	}

	[Fact]
	public void Federacao_ListaInvalida_LancaESemMudarAAnterior()
	{
		var federation = new TenantFederation(Guid.NewGuid(), Guid.NewGuid());
		federation.SetAllowedEmailDomains(["empresa.com"]);

		var act = () => federation.SetAllowedEmailDomains(["*.empresa.com"]);

		act.Should().Throw<DomainInvariantException>();
		federation.GetAllowedEmailDomains().Should().Equal("empresa.com");
	}

	[Fact]
	public void Mapeamento_NasceSemMarca_EMarcaDesmarcaSaoIdempotentes()
	{
		var mapping = new TenantGroupRoleMapping(Guid.NewGuid(), Guid.NewGuid(), "Grupo", Guid.NewGuid());

		mapping.IsProvisioningGroup.Should().BeFalse();

		mapping.MarkAsProvisioningGroup();
		mapping.MarkAsProvisioningGroup();
		mapping.IsProvisioningGroup.Should().BeTrue();

		mapping.UnmarkAsProvisioningGroup();
		mapping.UnmarkAsProvisioningGroup();
		mapping.IsProvisioningGroup.Should().BeFalse();
	}
}
