namespace Secco.SecureGate.Client.Administration;

/// <summary>
/// Credencial do client de gestão do próprio tenant (ADR-0052, subseção <c>Secco:SecureGate:TenantManagement</c>):
/// um client de PRODUTO, vinculado ao tenant da aplicação e criado pelo operador, separado do client de plataforma
/// da seção <c>Secco:SecureGate</c> — que segue servindo à resolução de permissões e ao catálogo. A URL base é a da
/// seção pai.
/// </summary>
public sealed class SecureGateTenantManagementOptions
{
	/// <summary>Chave da subseção de configuração.</summary>
	public const string SectionKey = "Secco:SecureGate:TenantManagement";

	/// <summary>Client id do client de produto com <c>securegate:tenant-manage</c>.</summary>
	public string? ClientId { get; set; }

	/// <summary>Client secret do client de produto. Nunca logar.</summary>
	public string? ClientSecret { get; set; }
}
