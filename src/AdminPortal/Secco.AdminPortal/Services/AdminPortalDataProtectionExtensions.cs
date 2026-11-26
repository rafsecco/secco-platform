using Microsoft.AspNetCore.DataProtection;

namespace Secco.AdminPortal.Services;

/// <summary>
/// Registro do DataProtection do AdminPortal. As chaves protegem o cookie de sessão e o antiforgery;
/// na imagem Docker elas ficam num volume, para que recriar o contêiner não as invalide.
/// </summary>
internal static class AdminPortalDataProtectionExtensions
{
	/// <summary>
	/// Registra o DataProtection com o nome de aplicação fixo <c>secco-adminportal</c> e, quando
	/// <c>AdminPortal:DataProtection:KeysPath</c> está preenchido, persiste o anel de chaves nessa pasta.
	/// Sem a chave de configuração o anel segue o padrão do framework (DEV local).
	/// </summary>
	internal static IServiceCollection AddAdminPortalDataProtection(this IServiceCollection services, IConfiguration configuration)
	{
		var builder = services.AddDataProtection().SetApplicationName("secco-adminportal");

		var keysPath = configuration["AdminPortal:DataProtection:KeysPath"];
		if (!string.IsNullOrWhiteSpace(keysPath))
		{
			builder.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
		}

		return services;
	}
}
