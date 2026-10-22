using Microsoft.Extensions.Options;
using Secco.SecureGate.Application.Permissions;

namespace Secco.SecureGate.Infrastructure.Seeding;

/// <summary>
/// Produtos ADOTANTES que publicam catálogo de permissões no SecureGate (seção <c>SecureGate:PermissionCatalog</c>,
/// issue #49, ADR-0039): o seed de referência registra um escopo <c>permissions:&lt;produto&gt;</c> para cada um, ao
/// lado dos da plataforma. Só a configuração da instalação decide — nunca a API, nem a declaração de um client.
/// Tirar um produto da lista não apaga o escopo: apagar quebraria clients e tokens que o usam.
/// </summary>
public sealed class PermissionCatalogOptions
{
	/// <summary>Seção de configuração.</summary>
	public const string SectionKey = "SecureGate:PermissionCatalog";

	/// <summary>Nomes de produto (kebab-case). Produto da plataforma na lista é ignorado: o escopo já existe.</summary>
	public List<string> Products { get; set; } = [];
}

/// <summary>
/// Validação fail-fast de <see cref="PermissionCatalogOptions"/> em toda subida: nome inválido nunca vira escopo, e a
/// regra é a mesma da rota de publicação, então todo escopo registrado é publicável.
/// </summary>
internal sealed class PermissionCatalogOptionsValidator : IValidateOptions<PermissionCatalogOptions>
{
	public ValidateOptionsResult Validate(string? name, PermissionCatalogOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var failures = options.Products
			.Select((product, index) => (product, index))
			.Where(entry => !PermissionCatalogProducts.IsValidName(entry.product))
			.Select(entry => $"{PermissionCatalogOptions.SectionKey}:Products:{entry.index} deve ser kebab-case minúsculo com até 50 caracteres.")
			.ToList();

		return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
	}
}
