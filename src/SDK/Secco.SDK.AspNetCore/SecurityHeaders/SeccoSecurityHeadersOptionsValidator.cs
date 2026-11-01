using Microsoft.Extensions.Options;

namespace Secco.SDK.AspNetCore.SecurityHeaders;

/// <summary>
/// Fail-fast da política na subida (ADR-0057). A gramática aceita só <c>'self'</c> e origens HTTPS/WSS,
/// então <c>'unsafe-inline'</c>, <c>'unsafe-eval'</c> e <c>*</c> nunca chegam ao cabeçalho, e nenhum
/// valor configurado carrega <c>;</c>, vírgula ou quebra de linha (injeção no cabeçalho).
/// </summary>
internal sealed class SeccoSecurityHeadersOptionsValidator : IValidateOptions<SeccoSecurityHeadersOptions>
{
	private const string Self = "'self'";
	private const string None = "'none'";
	private const int MaxPermissionLength = 64;

	public ValidateOptionsResult Validate(string? name, SeccoSecurityHeadersOptions options)
	{
		var failures = new List<string>();

		CheckSources(failures, "ScriptSources", options.ScriptSources, allowedSchemes: []);
		CheckSources(failures, "StyleSources", options.StyleSources, allowedSchemes: []);
		CheckSources(failures, "ImageSources", options.ImageSources, allowedSchemes: ["data:", "blob:"]);
		CheckSources(failures, "FontSources", options.FontSources, allowedSchemes: ["data:"]);
		CheckSources(failures, "ConnectSources", options.ConnectSources, allowedSchemes: []);
		CheckSources(failures, "FormActions", options.FormActions, allowedSchemes: []);

		if (options.FrameAncestors.Count == 0)
		{
			failures.Add("FrameAncestors não pode ficar vazio: use DenyFraming() para 'none'.");
		}
		else if (options.FrameAncestors.Contains(None))
		{
			if (options.FrameAncestors.Count > 1)
			{
				failures.Add("FrameAncestors com 'none' não admite outra origem.");
			}
		}
		else
		{
			CheckSources(failures, "FrameAncestors", options.FrameAncestors, allowedSchemes: []);
		}

		foreach (var permission in options.AllowedPermissions)
		{
			if (!IsPermissionName(permission))
			{
				failures.Add($"AllowedPermissions: '{permission}' não é nome de recurso válido.");
			}
		}

		if (options.ReportUri is { } reportUri
			&& (!reportUri.IsAbsoluteUri || reportUri.Scheme != Uri.UriSchemeHttps || !IsHeaderSafe(reportUri.OriginalString)))
		{
			failures.Add("ReportUri deve ser absoluta, HTTPS, sem ';', vírgula ou espaço.");
		}

		if (options.HstsEnabled && options.HstsMaxAge <= TimeSpan.Zero)
		{
			failures.Add("HstsMaxAge deve ser positivo (ou desligue com HstsEnabled = false).");
		}

		return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
	}

	private static void CheckSources(List<string> failures, string optionName, IEnumerable<string> sources, string[] allowedSchemes)
	{
		foreach (var source in sources)
		{
			if (source == Self || allowedSchemes.Contains(source, StringComparer.Ordinal) || IsSecureOrigin(source))
			{
				continue;
			}

			// Não ecoa o valor inteiro: pode conter quebra de linha e virar log forging.
			failures.Add($"{optionName}: origem inválida (só 'self', https:// ou wss://; nunca '*', aspas, ';', vírgula ou espaço).");
		}
	}

	private static bool IsSecureOrigin(string source)
	{
		if (string.IsNullOrEmpty(source) || !IsHeaderSafe(source) || source.Contains('*', StringComparison.Ordinal))
		{
			return false;
		}

		return Uri.TryCreate(source, UriKind.Absolute, out var uri)
			&& (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeWss)
			&& !string.IsNullOrEmpty(uri.Host)
			&& string.IsNullOrEmpty(uri.UserInfo)
			&& string.IsNullOrEmpty(uri.Query)
			&& string.IsNullOrEmpty(uri.Fragment);
	}

	/// <summary>Só caracteres de URL sem significado na sintaxe da CSP.</summary>
	private static bool IsHeaderSafe(string value)
	{
		foreach (var c in value)
		{
			var allowed = char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~' or ':' or '/' or '%';

			if (!allowed)
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsPermissionName(string value)
	{
		if (string.IsNullOrEmpty(value) || value.Length > MaxPermissionLength || !char.IsAsciiLetterLower(value[0]))
		{
			return false;
		}

		foreach (var c in value)
		{
			if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-')
			{
				return false;
			}
		}

		return true;
	}
}
