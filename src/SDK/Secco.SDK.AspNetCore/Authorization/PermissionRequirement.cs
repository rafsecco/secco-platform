using Microsoft.AspNetCore.Authorization;

namespace Secco.SDK.AspNetCore.Authorization;

/// <summary>Exigência de uma permissão <c>recurso:acao</c> (ADR-0021) no endpoint.</summary>
/// <param name="permission">Permissão exigida, no formato canônico.</param>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    /// <summary>Permissão exigida (<c>recurso:acao</c>).</summary>
    public string Permission { get; } = permission;
}
