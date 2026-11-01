using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Secco.SDK.AspNetCore.SecurityHeaders;

/// <summary>
/// Cabeçalhos montados uma vez, na subida, a partir das options já validadas (ADR-0057).
/// Por requisição só entra o nonce, entre partes fixas da CSP.
/// </summary>
internal sealed class SeccoSecurityHeadersPolicy
{
	/// <summary>Recursos sensíveis negados por padrão no <c>Permissions-Policy</c>.</summary>
	internal static readonly string[] DeniedPermissions =
	[
		"accelerometer", "camera", "display-capture", "geolocation", "gyroscope",
		"magnetometer", "microphone", "midi", "payment", "serial", "usb",
	];

	private readonly string _cspBeforeScriptNonce;
	private readonly string _cspBeforeStyleNonce;
	private readonly string _cspAfterStyleNonce;

	private SeccoSecurityHeadersPolicy(SeccoSecurityHeadersOptions options)
	{
		CspHeaderName = options.Mode == SeccoCspMode.ReportOnly
			? "Content-Security-Policy-Report-Only"
			: "Content-Security-Policy";

		_cspBeforeScriptNonce = "default-src 'self'; script-src 'self' 'nonce-";
		_cspBeforeStyleNonce = "'" + Extra(options.ScriptSources) + "; style-src 'self' 'nonce-";

		var after = new StringBuilder("'").Append(Extra(options.StyleSources));
		after.Append("; style-src-attr ").Append(StyleAttributeSources(options.StyleAttributeValues));
		after.Append("; img-src 'self' data:").Append(Extra(options.ImageSources));

		if (options.FontSources.Count > 0)
		{
			after.Append("; font-src 'self'").Append(Extra(options.FontSources));
		}

		if (options.ConnectSources.Count > 0)
		{
			after.Append("; connect-src 'self'").Append(Extra(options.ConnectSources));
		}

		after.Append("; object-src 'none'; base-uri 'self'; frame-ancestors ").AppendJoin(' ', options.FrameAncestors);

		if (options.FormActions.Count > 0)
		{
			after.Append("; form-action ").AppendJoin(' ', options.FormActions);
		}

		if (options.ReportUri is { } reportUri)
		{
			after.Append("; report-uri ").Append(reportUri.OriginalString);
		}

		_cspAfterStyleNonce = after.ToString();

		XFrameOptions = options.FrameAncestors switch
		{
			["'none'"] => "DENY",
			["'self'"] => "SAMEORIGIN",
			_ => null, // lista de outras origens: X-Frame-Options não consegue expressar.
		};

		PermissionsPolicy = string.Join(", ", DeniedPermissions
			.Where(p => !options.AllowedPermissions.Contains(p, StringComparer.Ordinal))
			.Select(p => p + "=()")
			.Concat(options.AllowedPermissions.Distinct(StringComparer.Ordinal).Select(p => p + "=(self)")));

		if (options.HstsEnabled)
		{
			var hsts = new StringBuilder("max-age=")
				.Append(((long)options.HstsMaxAge.TotalSeconds).ToString(CultureInfo.InvariantCulture));

			if (options.HstsIncludeSubDomains)
			{
				hsts.Append("; includeSubDomains");
			}

			if (options.HstsPreload)
			{
				hsts.Append("; preload");
			}

			StrictTransportSecurity = hsts.ToString();
		}
	}

	internal string CspHeaderName { get; }

	internal string? XFrameOptions { get; }

	internal string PermissionsPolicy { get; }

	internal string? StrictTransportSecurity { get; }

	internal static SeccoSecurityHeadersPolicy Create(SeccoSecurityHeadersOptions options) => new(options);

	internal string ContentSecurityPolicy(string nonce)
		=> string.Concat(_cspBeforeScriptNonce, nonce, _cspBeforeStyleNonce, nonce, _cspAfterStyleNonce);

	/// <summary>Hash SHA-256 em base64 do valor exato, no formato de fonte da CSP.</summary>
	internal static string HashSource(string value)
		=> "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value))) + "'";

	private static string Extra(IEnumerable<string> sources)
	{
		var distinct = sources.Where(s => s != "'self'").Distinct(StringComparer.Ordinal).ToList();
		return distinct.Count == 0 ? string.Empty : " " + string.Join(' ', distinct);
	}

	private static string StyleAttributeSources(List<string> values)
		=> values.Count == 0
			? "'none'"
			: "'unsafe-hashes' " + string.Join(' ', values.Distinct(StringComparer.Ordinal).Select(HashSource));
}
