using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Secco.SDK.AspNetCore.Authentication;

/// <summary>
/// Fail-fast de configuração de autenticação no startup (ADR-0020): uma API da plataforma
/// jamais sobe sem autenticação válida, e jamais sobe em Production com chave de desenvolvimento.
/// </summary>
internal sealed class SeccoAuthenticationOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<SeccoAuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, SeccoAuthenticationOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add($"'{SeccoAuthenticationOptions.SectionKey}:Audience' é obrigatória.");
        }

        var hasAuthority = !string.IsNullOrWhiteSpace(options.Authority);
        var hasDevelopmentKey = !string.IsNullOrWhiteSpace(options.DevelopmentSigningKey);

        if (!hasAuthority && !hasDevelopmentKey)
        {
            failures.Add(
                $"Configure '{SeccoAuthenticationOptions.SectionKey}:Authority' (OIDC) ou " +
                $"'{SeccoAuthenticationOptions.SectionKey}:DevelopmentSigningKey' (apenas fora de Production).");
        }

        if (hasAuthority && hasDevelopmentKey)
        {
            failures.Add("Authority e DevelopmentSigningKey são mutuamente exclusivos — configuração ambígua.");
        }

        if (hasDevelopmentKey)
        {
            if (environment.IsProduction())
            {
                failures.Add("DevelopmentSigningKey é proibida em Production — use Authority (OIDC).");
            }

            if (options.DevelopmentSigningKey!.Length < SeccoAuthenticationOptions.MinimumSigningKeyLength)
            {
                failures.Add($"DevelopmentSigningKey exige ao menos {SeccoAuthenticationOptions.MinimumSigningKeyLength} caracteres (HS256).");
            }

            if (string.IsNullOrWhiteSpace(options.Issuer))
            {
                failures.Add("Issuer é obrigatório no modo DevelopmentSigningKey.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
