using Microsoft.Extensions.Configuration;
using Secco.SharedKernel.Authorization;

namespace Secco.SDK.AspNetCore.Authorization;

/// <summary>
/// Implementação padrão do <see cref="IPermissionResolver"/> sobre <c>IConfiguration</c> —
/// para DEV e testes, espelhando o <see cref="Tenancy.ConfigurationTenantCatalog"/>. O
/// mapeamento é global (o tenant é ignorado): customização por tenant é semântica do
/// SecureGate (ADR-0021), que assume fora de DEV via <c>Secco.SecureGate.Client</c>.
/// Formato esperado:
/// <code>
/// "Secco": { "Authorization": { "Roles": {
///     "&lt;role&gt;": { "Permissions": [ "log-entries:read", "log-entries:write" ] }
/// } } }
/// </code>
/// Entradas fora do formato canônico <c>recurso:acao</c> são ignoradas silenciosamente.
/// </summary>
public sealed class ConfigurationPermissionResolver(IConfiguration configuration) : IPermissionResolver
{
    /// <summary>Chave da seção de configuração onde os roles são declarados.</summary>
    internal const string RolesSectionKey = SeccoAuthorizationOptions.SectionKey + ":Roles";

    /// <inheritdoc />
    public ValueTask<IReadOnlySet<string>> ResolveAsync(
        Guid tenantId,
        string role,
        CancellationToken cancellationToken = default)
    {
        var permissions = configuration
            .GetSection($"{RolesSectionKey}:{role}:Permissions")
            .GetChildren()
            .Select(entry => entry.Value)
            .Where(value => SeccoPermissions.IsValid(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.Ordinal);

        return ValueTask.FromResult<IReadOnlySet<string>>(permissions);
    }
}
