using System.Net;
using Microsoft.Extensions.Options;

namespace Secco.SDK.AspNetCore.ReverseProxy;

/// <summary>Fail-fast das opções de proxy reverso na subida (ADR-0062).</summary>
internal sealed class SeccoReverseProxyOptionsValidator : IValidateOptions<SeccoReverseProxyOptions>
{
	public ValidateOptionsResult Validate(string? name, SeccoReverseProxyOptions options)
	{
		if (!options.Enabled)
		{
			return ValidateOptionsResult.Success;
		}

		if (options.KnownNetworks.Count == 0)
		{
			return ValidateOptionsResult.Fail(
				$"{SeccoReverseProxyOptions.SectionKey}:KnownNetworks é obrigatória quando Enabled=true (ADR-0062).");
		}

		var failures = new List<string>();

		for (var i = 0; i < options.KnownNetworks.Count; i++)
		{
			if (!IPNetwork.TryParse(options.KnownNetworks[i], out _))
			{
				failures.Add($"{SeccoReverseProxyOptions.SectionKey}:KnownNetworks:{i} não é um CIDR válido.");
			}
		}

		return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
	}
}
