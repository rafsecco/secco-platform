using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;

namespace Secco.SDK.AspNetCore.Authorization;

/// <summary>
/// Avalia <see cref="PermissionRequirement"/> (ADR-0021): o token carrega apenas
/// <c>role</c>; as permissões do par <c>(tenant, role)</c> são resolvidas em runtime
/// via <see cref="CachedPermissionResolver"/>. <b>Fail-closed em tudo</b>: sem tenant
/// resolvido, sem roles no token ou resolução indisponível — o requirement simplesmente
/// não é satisfeito (403). Autorização nunca falha aberta.
/// </summary>
internal sealed class PermissionAuthorizationHandler(
    CachedPermissionResolver resolver,
    ITenantContext tenantContext,
    ILogger<PermissionAuthorizationHandler> logger) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            // Permissões são POR TENANT (ADR-0021) — sem tenant não há o que conceder
            return;
        }

        var roles = context.User.FindAll(SeccoClaims.Role)
            .SelectMany(static claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal);

        foreach (var role in roles)
        {
            IReadOnlySet<string> permissions;

            try
            {
                permissions = await resolver.ResolveAsync(tenantId, role, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Fail-closed (ADR-0021): resolução indisponível nega — nunca concede
                AuthorizationLog.PermissionResolutionFailed(logger, role, exception);

                continue;
            }

            if (permissions.Contains(requirement.Permission))
            {
                context.Succeed(requirement);

                return;
            }
        }
    }
}
