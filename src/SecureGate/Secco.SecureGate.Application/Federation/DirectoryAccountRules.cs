using System.Net.Mail;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.Tenants;

namespace Secco.SecureGate.Application.Federation;

/// <summary>Por que o e-mail de uma pessoa do diretório não virou conta (ADR-0055).</summary>
public enum EmailRefusalReason
{
	/// <summary>Nem <c>mail</c> nem o UPN têm formato de e-mail utilizável.</summary>
	NoUsableEmail,

	/// <summary>O domínio do e-mail não está na lista de domínios permitidos da federação.</summary>
	DomainNotAllowed,

	/// <summary>O e-mail já pertence a outra conta da instalação.</summary>
	EmailInUse,
}

/// <summary>Valores de <see cref="EmailRefusalReason"/> como a trilha os grava (texto estável, só do servidor).</summary>
public static class EmailRefusalReasons
{
	/// <summary>Sem e-mail utilizável.</summary>
	public const string NoUsableEmail = "no-usable-email";

	/// <summary>Domínio fora da lista permitida.</summary>
	public const string DomainNotAllowed = "domain-not-allowed";

	/// <summary>E-mail já em uso.</summary>
	public const string EmailInUse = "email-in-use";

	/// <summary>Texto da trilha para o motivo.</summary>
	/// <param name="reason">Motivo.</param>
	public static string From(EmailRefusalReason reason) => reason switch
	{
		EmailRefusalReason.NoUsableEmail => NoUsableEmail,
		EmailRefusalReason.DomainNotAllowed => DomainNotAllowed,
		_ => EmailInUse,
	};
}

/// <summary>E-mail e nome já validados de uma pessoa do diretório, prontos para virar conta.</summary>
/// <param name="Email">E-mail minúsculo, de formato validado, num domínio permitido.</param>
/// <param name="DisplayName">Nome de exibição validado, ou <c>null</c> se o do diretório não passou na validação.</param>
public sealed record DirectoryAccountDraft(string Email, string? DisplayName);

/// <summary>
/// Validação do que o Graph devolve antes de virar conta (ADR-0055/0020). Nome e e-mail do diretório do cliente são
/// input externo: tamanho, formato e caractere de controle são conferidos aqui, e nada passa por confiança.
/// </summary>
public static class DirectoryAccountRules
{
	/// <summary>Tamanho máximo do e-mail (o mesmo do cadastro por admin).</summary>
	public const int EmailMaxLength = 256;

	/// <summary>
	/// Monta o rascunho da conta: e-mail = <c>mail</c>, senão o UPN se ele tiver formato de e-mail; o domínio precisa
	/// estar na lista permitida. Nome inválido (caractere de controle, longo demais) é descartado — a conta nasce sem
	/// nome em vez de recusar a pessoa por um enfeite.
	/// </summary>
	/// <param name="person">Pessoa lida do Graph (textos crus).</param>
	/// <param name="allowedDomains">Domínios permitidos da federação, já normalizados.</param>
	/// <param name="draft">O rascunho, quando aceito.</param>
	/// <param name="refusal">O motivo, quando recusado.</param>
	/// <returns><c>true</c> se a pessoa pode virar conta.</returns>
	public static bool TryBuildDraft(
		DirectoryPerson person,
		IReadOnlyCollection<string> allowedDomains,
		out DirectoryAccountDraft? draft,
		out EmailRefusalReason refusal)
	{
		ArgumentNullException.ThrowIfNull(person);
		ArgumentNullException.ThrowIfNull(allowedDomains);

		draft = null;
		refusal = EmailRefusalReason.NoUsableEmail;

		if (!TryNormalizeEmail(person.Mail, out var email, out var domain)
			&& !TryNormalizeEmail(person.UserPrincipalName, out email, out domain))
		{
			return false;
		}

		if (!allowedDomains.Contains(domain, StringComparer.Ordinal))
		{
			refusal = EmailRefusalReason.DomainNotAllowed;

			return false;
		}

		var name = DisplayNameRules.Normalize(person.DisplayName);
		draft = new DirectoryAccountDraft(email, name is not null && DisplayNameRules.IsValid(name) ? name : null);

		return true;
	}

	/// <summary>
	/// Aceita só <c>local@dominio</c> puro: sem nome de exibição, sem comentário, sem espaço, sem caractere de
	/// controle, com domínio de hostname válido. Devolve o e-mail em minúsculas.
	/// </summary>
	/// <param name="input">Texto cru do diretório.</param>
	/// <param name="email">E-mail normalizado.</param>
	/// <param name="domain">Domínio normalizado.</param>
	public static bool TryNormalizeEmail(string? input, out string email, out string domain)
	{
		email = string.Empty;
		domain = string.Empty;

		if (string.IsNullOrWhiteSpace(input) || input.Length > EmailMaxLength || input.Any(char.IsControl))
		{
			return false;
		}

		var trimmed = input.Trim();

		if (!MailAddress.TryCreate(trimmed, out var parsed)
			|| parsed.DisplayName.Length != 0
			|| !string.Equals(parsed.Address, trimmed, StringComparison.Ordinal)
			|| trimmed.Any(char.IsWhiteSpace)
			|| !EmailDomainRules.TryNormalize(parsed.Host, out domain))
		{
			return false;
		}

		email = $"{parsed.User.ToLowerInvariant()}@{domain}";

		return true;
	}
}
