using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Clients;

namespace Secco.SecureGate.Infrastructure.Clients;

/// <summary>
/// Validação fail-fast de <c>SecureGate:PlatformClients</c> (ADR-0037) — roda em toda subida, em
/// qualquer ambiente, mesmo onde o seed não roda. A mensagem nomeia índice e campo, nunca o secret.
/// </summary>
internal sealed partial class PlatformClientsOptionsValidator(IHostEnvironment environment)
	: IValidateOptions<PlatformClientsOptions>
{
	private const int ClientIdMaxLength = 100;
	private const int MinimumSecretLength = 32;

	[GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
	private static partial Regex ClientIdPattern();

	public ValidateOptionsResult Validate(string? name, PlatformClientsOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var failures = new List<string>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var development = environment.IsDevelopment();

		for (var i = 0; i < options.PlatformClients.Count; i++)
		{
			var client = options.PlatformClients[i];
			var at = $"SecureGate:PlatformClients:{i}";

			if (string.IsNullOrEmpty(client.ClientId) || client.ClientId.Length > ClientIdMaxLength
				|| !ClientIdPattern().IsMatch(client.ClientId))
			{
				failures.Add($"{at}:ClientId deve ser kebab-case minúsculo com até {ClientIdMaxLength} caracteres.");
			}
			else if (client.ClientId.StartsWith(ProductClientRules.ClientIdPrefix, StringComparison.Ordinal))
			{
				failures.Add($"{at}:ClientId não pode começar com '{ProductClientRules.ClientIdPrefix}', reservado à API.");
			}
			else if (!seen.Add(client.ClientId))
			{
				failures.Add($"{at}:ClientId '{client.ClientId}' repetido.");
			}

			if (client.Type is null)
			{
				failures.Add($"{at}:Type é obrigatório (ClientCredentials ou AuthorizationCode).");
			}

			if (string.IsNullOrEmpty(client.ClientSecret))
			{
				failures.Add($"{at}:ClientSecret é obrigatório.");
			}
			else if (!development && client.ClientSecret.Length < MinimumSecretLength)
			{
				failures.Add($"{at}:ClientSecret precisa de ao menos {MinimumSecretLength} caracteres fora de Development.");
			}

			if (client.Scopes.Contains(SecureGateScopes.DirectoryRead, StringComparer.Ordinal))
			{
				failures.Add($"{at}:Scopes não pode conter '{SecureGateScopes.DirectoryRead}': só client de produto vinculado a tenant o recebe (ADR-0045).");
			}

			if (client.Scopes.Contains(SecureGateScopes.TenantManage, StringComparer.Ordinal))
			{
				failures.Add($"{at}:Scopes não pode conter '{SecureGateScopes.TenantManage}': só client de produto vinculado a tenant o recebe (ADR-0052).");
			}

			if (client.Type == PlatformClientType.AuthorizationCode)
			{
				if (client.RedirectUris.Count == 0)
				{
					failures.Add($"{at}:RedirectUris é obrigatório para AuthorizationCode.");
				}

				foreach (var uri in client.RedirectUris.Concat(client.PostLogoutRedirectUris))
				{
					// A mesma regra da API de clients interativos (ADR-0040): uma só definição
					if (!ProductClientRules.IsValidRedirectUri(uri, development))
					{
						failures.Add($"{at}: URI '{uri}' inválida — absoluta e, fora de Development, https.");
					}
				}
			}
		}

		return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
	}
}
