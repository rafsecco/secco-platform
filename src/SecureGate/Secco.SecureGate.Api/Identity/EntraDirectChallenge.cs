using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Desafio direto ao Entra ID (login federado direto, ADR-0026): monta as propriedades do desafio e, no
/// evento de redirecionamento, fixa o endpoint de autorização no diretório do tenant e repassa as dicas.
/// </summary>
internal static class EntraDirectChallenge
{
	private const string DirectoryItem = "secco.direct.directory";
	private const string DomainHintItem = "secco.direct.domain_hint";
	private const string LoginHintItem = "secco.direct.login_hint";
	private const string ForceLoginItem = "secco.direct.force_login";

	/// <summary>Propriedades do desafio; o <paramref name="loginHint"/> já chega sanitizado.</summary>
	/// <param name="localReturnUrl">Destino local para onde o callback devolve a pessoa.</param>
	/// <param name="target">Diretório e dica de domínio do tenant.</param>
	/// <param name="loginHint">Dica de e-mail já sanitizada, ou <c>null</c>.</param>
	/// <param name="forceLogin">Exige nova autenticação (<c>prompt=login</c>).</param>
	public static AuthenticationProperties Create(string localReturnUrl, DirectSignInTarget target, string? loginHint, bool forceLogin)
	{
		var properties = new AuthenticationProperties
		{
			RedirectUri = "/login/entra-callback?returnUrl=" + Uri.EscapeDataString(localReturnUrl),
		};

		properties.Items[DirectoryItem] = target.DirectoryId.ToString("D");

		if (!string.IsNullOrEmpty(target.DomainHint))
		{
			properties.Items[DomainHintItem] = target.DomainHint;
		}

		if (!string.IsNullOrEmpty(loginHint))
		{
			properties.Items[LoginHintItem] = loginHint;
		}

		if (forceLogin)
		{
			properties.Items[ForceLoginItem] = "1";
		}

		return properties;
	}

	/// <summary>Aplica diretório, dicas e prompt ao redirecionamento; sem o item de diretório, não faz nada.</summary>
	/// <param name="context">Contexto do evento <c>OnRedirectToIdentityProvider</c>.</param>
	public static Task OnRedirectToIdentityProvider(RedirectContext context)
	{
		if (!context.Properties.Items.TryGetValue(DirectoryItem, out var directory)
			|| !Guid.TryParse(directory, out var directoryId))
		{
			return Task.CompletedTask;
		}

		var message = context.ProtocolMessage;

		if (message.IssuerAddress is not null)
		{
			message.IssuerAddress = PinDirectory(message.IssuerAddress, directoryId) ?? message.IssuerAddress;
		}

		if (context.Properties.Items.TryGetValue(DomainHintItem, out var domainHint))
		{
			message.DomainHint = domainHint;
		}

		if (context.Properties.Items.TryGetValue(LoginHintItem, out var loginHint))
		{
			message.LoginHint = loginHint;
		}

		if (context.Properties.Items.ContainsKey(ForceLoginItem))
		{
			message.Prompt = "login";
		}

		return Task.CompletedTask;
	}

	/// <summary>
	/// Troca o primeiro segmento (<c>organizations</c> ou <c>common</c>) do endpoint pelo diretório. Só URI
	/// absoluta https; qualquer outro caso devolve <c>null</c>.
	/// </summary>
	/// <param name="authorizationEndpoint">Endpoint de autorização da configuração do esquema.</param>
	/// <param name="directoryId">Diretório do tenant.</param>
	public static string? PinDirectory(string authorizationEndpoint, Guid directoryId)
	{
		if (!Uri.TryCreate(authorizationEndpoint, UriKind.Absolute, out var uri)
			|| uri.Scheme != Uri.UriSchemeHttps)
		{
			return null;
		}

		var segments = uri.AbsolutePath.Split('/');

		// "/organizations/..." => ["", "organizations", ...]
		if (segments.Length < 2
			|| !(segments[1].Equals("organizations", StringComparison.OrdinalIgnoreCase)
				|| segments[1].Equals("common", StringComparison.OrdinalIgnoreCase)))
		{
			return null;
		}

		segments[1] = directoryId.ToString("D");

		return new UriBuilder(uri) { Path = string.Join('/', segments) }.Uri.AbsoluteUri;
	}
}
