using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Secco.SharedKernel.Authorization;

namespace Secco.SDK.AspNetCore.Authorization;

/// <summary>
/// Provider de policies dinâmicas da ADR-0021: um nome de policy no formato canônico
/// <c>recurso:acao</c> vira automaticamente uma policy com <see cref="PermissionRequirement"/> —
/// os produtos usam suas constantes direto (<c>RequireAuthorization(LogStreamPermissions.LogEntries.Write)</c>)
/// sem registrar nada. Nomes fora do formato delegam ao provider padrão (policies nomeadas,
/// Default e a FallbackPolicy fail-closed seguem intactos).
/// </summary>
internal sealed class SeccoPermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
        SeccoPermissions.IsValid(policyName)
            ? Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName))
                .Build())
            : _fallback.GetPolicyAsync(policyName);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
