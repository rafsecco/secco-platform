using Microsoft.AspNetCore.Builder;
using Secco.SDK.AspNetCore.SecurityHeaders;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição de pipeline para os cabeçalhos de segurança de resposta (ADR-0057).</summary>
public static class SeccoSecurityHeadersApplicationBuilderExtensions
{
	/// <summary>
	/// Adiciona o middleware que põe os cabeçalhos de segurança em toda resposta. Deve vir cedo:
	/// depois do tratamento de exceção e antes dos arquivos estáticos, para cobri-los também.
	/// Requer <c>AddSeccoSecurityHeaders()</c> registrado no DI.
	/// </summary>
	/// <param name="app">Pipeline de middlewares da aplicação.</param>
	public static IApplicationBuilder UseSeccoSecurityHeaders(this IApplicationBuilder app)
	{
		ArgumentNullException.ThrowIfNull(app);

		return app.UseMiddleware<SeccoSecurityHeadersMiddleware>();
	}
}
