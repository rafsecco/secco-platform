using FluentAssertions;
using Secco.SecureGate.Application.Federation;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// O que o Graph devolve é input externo (ADR-0020/0055): e-mail e nome só viram conta depois de validados, e o
/// e-mail precisa estar num domínio permitido da federação.
/// </summary>
public class DirectoryAccountRulesTests
{
	private static readonly string[] Allowed = ["secco.test"];

	private static DirectoryPerson Person(string? mail, string? upn = null, string? name = "Maria Souza") =>
		new(Guid.NewGuid(), name, mail, upn);

	private static bool Build(DirectoryPerson person, out DirectoryAccountDraft? draft, out EmailRefusalReason reason) =>
		DirectoryAccountRules.TryBuildDraft(person, Allowed, out draft, out reason);

	[Fact]
	public void Mail_ValidoNoDominioPermitido_ViraConta_EmMinusculas()
	{
		Build(Person("Maria.Souza@Secco.Test"), out var draft, out _).Should().BeTrue();

		draft!.Email.Should().Be("maria.souza@secco.test");
		draft.DisplayName.Should().Be("Maria Souza");
	}

	[Fact]
	public void SemMail_UsaOUpn_QuandoTemFormatoDeEmail()
	{
		Build(Person(mail: null, upn: "maria@secco.test"), out var draft, out _).Should().BeTrue();

		draft!.Email.Should().Be("maria@secco.test");
	}

	[Fact]
	public void MailInvalido_CaiNoUpn()
	{
		Build(Person(mail: "isto nao e email", upn: "maria@secco.test"), out var draft, out _).Should().BeTrue();

		draft!.Email.Should().Be("maria@secco.test");
	}

	[Theory]
	[InlineData("maria")]
	[InlineData("maria#EXT#")]
	public void UpnSemFormatoDeEmail_NaoServeDeEmail(string upn)
	{
		Build(Person(mail: null, upn: upn), out var draft, out var reason).Should().BeFalse();

		draft.Should().BeNull();
		reason.Should().Be(EmailRefusalReason.NoUsableEmail);
	}

	[Fact]
	public void SemMailNemUpn_Recusa()
	{
		Build(Person(null, null), out _, out var reason).Should().BeFalse();

		reason.Should().Be(EmailRefusalReason.NoUsableEmail);
	}

	[Fact]
	public void DominioForaDaLista_Recusa_ComMotivoPropio()
	{
		Build(Person("maria@outra.com"), out var draft, out var reason).Should().BeFalse();

		draft.Should().BeNull();
		reason.Should().Be(EmailRefusalReason.DomainNotAllowed);
	}

	[Fact]
	public void ListaDeDominiosVazia_RecusaTodoEmail()
	{
		DirectoryAccountRules.TryBuildDraft(Person("maria@secco.test"), [], out _, out var reason).Should().BeFalse();

		reason.Should().Be(EmailRefusalReason.DomainNotAllowed);
	}

	[Fact]
	public void SubdominioNaoCasaComODominioPermitido()
	{
		// Sem curinga: permitir secco.test não permite sub.secco.test nem um domínio que apenas termine igual
		Build(Person("maria@sub.secco.test"), out _, out var sub).Should().BeFalse();
		Build(Person("maria@naosecco.test"), out _, out var suffix).Should().BeFalse();

		sub.Should().Be(EmailRefusalReason.DomainNotAllowed);
		suffix.Should().Be(EmailRefusalReason.DomainNotAllowed);
	}

	[Theory]
	[InlineData("\"Maria\" <maria@secco.test>")]
	[InlineData("maria@secco.test, outro@secco.test")]
	[InlineData("maria@secco.test\r\nBcc: x@y.z")]
	[InlineData("ma ria@secco.test")]
	[InlineData("maria@secco.test (comentario)")]
	[InlineData("maria@[10.0.0.1]")]
	public void EmailComFormaDeCabecalhoOuControle_NaoEEmailUtilizavel(string mail)
	{
		Build(Person(mail), out _, out var reason).Should().BeFalse();

		reason.Should().Be(EmailRefusalReason.NoUsableEmail);
	}

	[Fact]
	public void EmailAcimaDe256Caracteres_Recusa()
	{
		var local = new string('a', 250);

		Build(Person($"{local}@secco.test"), out _, out var reason).Should().BeFalse();

		reason.Should().Be(EmailRefusalReason.NoUsableEmail);
	}

	[Theory]
	[InlineData("Maria\nSouza")]
	[InlineData("Maria\u0007")]
	public void NomeComCaractereDeControle_EDescartado_ASContaNasceSemNome(string name)
	{
		Build(Person("maria@secco.test", name: name), out var draft, out _).Should().BeTrue();

		draft!.DisplayName.Should().BeNull("o nome é enfeite: não recusa a pessoa");
	}

	[Fact]
	public void NomeAcimaDoLimite_EDescartado()
	{
		Build(Person("maria@secco.test", name: new string('x', 161)), out var draft, out _).Should().BeTrue();

		draft!.DisplayName.Should().BeNull();
	}

	[Fact]
	public void NomeEmBranco_ViraNulo_ENomeComEspacosNasPontasEAparado()
	{
		Build(Person("a@secco.test", name: "   "), out var blank, out _).Should().BeTrue();
		Build(Person("b@secco.test", name: "  Maria  "), out var padded, out _).Should().BeTrue();

		blank!.DisplayName.Should().BeNull();
		padded!.DisplayName.Should().Be("Maria");
	}

	[Fact]
	public void MailTemPrioridadeSobreOUpn_EDominioForaNaoCaiNoUpn()
	{
		// O primeiro e-mail VÁLIDO decide: se ele está fora da lista, a pessoa é recusada — não se procura outro
		Build(Person(mail: "maria@outra.com", upn: "maria@secco.test"), out _, out var reason).Should().BeFalse();

		reason.Should().Be(EmailRefusalReason.DomainNotAllowed);
	}
}
