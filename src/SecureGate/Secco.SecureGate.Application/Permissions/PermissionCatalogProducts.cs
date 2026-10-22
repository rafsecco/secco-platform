using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Permissions;

namespace Secco.SecureGate.Application.Permissions;

/// <summary>
/// Nome de produto do catálogo de permissões (ADR-0039): uma regra só para a rota de publicação e para os produtos
/// declarados pela instalação (issue #49) — todo escopo <c>permissions:&lt;produto&gt;</c> registrado é publicável.
/// </summary>
public static class PermissionCatalogProducts
{
	/// <summary>Produtos da própria plataforma, sempre registrados.</summary>
	public static readonly IReadOnlyList<string> Platform = ["logstream", "notificationhub"];

	/// <summary>Indica se o nome é válido: kebab-case minúsculo, até o limite da coluna. Sem normalização de caixa.</summary>
	/// <param name="product">Nome candidato.</param>
	public static bool IsValidName(string? product) =>
		TenantInputRules.IsValidSlug(product ?? string.Empty, PermissionCatalogEntry.ProductMaxLength);
}
