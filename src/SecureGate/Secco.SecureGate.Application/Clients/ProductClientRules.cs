using System.Security.Cryptography;

namespace Secco.SecureGate.Application.Clients;

/// <summary>Regras de formato e geração de credencial do client de produto (ADR-0037, ADR-0020).</summary>
public static class ProductClientRules
{
	/// <summary>Tamanho máximo do nome.</summary>
	public const int NameMaxLength = 100;

	/// <summary>Máximo de escopos por client.</summary>
	public const int MaxScopes = 10;

	/// <summary>Máximo de papéis por client.</summary>
	public const int MaxRoles = 20;

	/// <summary>Tipo de client de máquina (padrão quando o tipo não é informado).</summary>
	public const string ClientCredentialsType = "client_credentials";

	/// <summary>Tipo de client interativo (login de pessoa por authorization code + PKCE, ADR-0040).</summary>
	public const string AuthorizationCodeType = "authorization_code";

	/// <summary>Máximo de redirect URIs (e de post-logout URIs) por client interativo.</summary>
	public const int MaxRedirectUris = 10;

	/// <summary>Tamanho máximo de uma redirect URI.</summary>
	public const int RedirectUriMaxLength = 2000;

	/// <summary>Escopos OIDC padrão que todo client interativo recebe além dos de produto.</summary>
	public static readonly IReadOnlyList<string> InteractiveStandardScopes =
		["openid", "profile", "email", "roles", "offline_access"];

	/// <summary>
	/// Prefixo de todo <c>client_id</c> gerado pela API. A configuração de plataforma é proibida de
	/// usá-lo, então os dois espaços de nome nunca se cruzam.
	/// </summary>
	public const string ClientIdPrefix = "cli_";

	private const string Base32Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

	/// <summary>Gera um <c>client_id</c> aleatório — o chamador nunca escolhe (sem colisão nem imitação).</summary>
	public static string NewClientId()
	{
		Span<char> suffix = stackalloc char[16];

		for (var i = 0; i < suffix.Length; i++)
		{
			suffix[i] = Base32Alphabet[RandomNumberGenerator.GetInt32(Base32Alphabet.Length)];
		}

		return ClientIdPrefix + new string(suffix);
	}

	/// <summary>Gera um secret de 32 bytes aleatórios em base64url, exibido uma única vez.</summary>
	public static string NewSecret() =>
		Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
			.TrimEnd('=').Replace('+', '-').Replace('/', '_');

	/// <summary>Valida um nome já aparado: 1–100 caracteres, sem caractere de controle.</summary>
	/// <param name="name">Nome candidato.</param>
	public static bool IsValidName(string name) =>
		name.Length is > 0 and <= NameMaxLength && !name.Any(char.IsControl);

	/// <summary>
	/// Regra única de redirect URI (client interativo da API e <c>SecureGate:PlatformClients</c>):
	/// absoluta, http(s) sem credencial nem fragmento, até <see cref="RedirectUriMaxLength"/>
	/// caracteres e HTTPS, salvo onde o http é tolerado (Development).
	/// </summary>
	/// <param name="uri">URI candidata.</param>
	/// <param name="allowInsecure">Aceita <c>http://</c> (só em Development).</param>
	public static bool IsValidRedirectUri(string? uri, bool allowInsecure) =>
		!string.IsNullOrEmpty(uri)
		&& uri.Length <= RedirectUriMaxLength
		&& Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
		&& parsed.UserInfo.Length == 0
		&& string.IsNullOrEmpty(parsed.Fragment)
		&& !uri.Contains('#', StringComparison.Ordinal)
		&& (parsed.Scheme == Uri.UriSchemeHttps || (allowInsecure && parsed.Scheme == Uri.UriSchemeHttp));
}
