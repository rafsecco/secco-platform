using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;

namespace Secco.SDK.AspNetCore.SecurityHeaders;

/// <summary>
/// Nonce da CSP da requisição atual (ADR-0057), para componentes Razor renderizados no servidor.
/// Em MVC e Razor Pages, prefira a tag helper <c>secco-nonce</c>.
/// </summary>
public interface ISeccoCspNonce
{
	/// <summary>
	/// Valor do nonce. Ler marca a resposta como não armazenável em cache compartilhado.
	/// </summary>
	/// <exception cref="InvalidOperationException"><c>UseSeccoSecurityHeaders()</c> não está no pipeline.</exception>
	string Value { get; }
}

/// <summary>Acesso ao nonce da CSP a partir do <see cref="HttpContext"/> (ADR-0057).</summary>
public static class SeccoCspNonceHttpContextExtensions
{
	/// <summary>Devolve o nonce da requisição e marca a resposta como não armazenável em cache.</summary>
	/// <exception cref="InvalidOperationException"><c>UseSeccoSecurityHeaders()</c> não está no pipeline.</exception>
	public static string GetSeccoCspNonce(this HttpContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var feature = context.Features.Get<SeccoCspNonceFeature>()
			?? throw new InvalidOperationException("O nonce da CSP exige UseSeccoSecurityHeaders() no pipeline, antes do endpoint.");

		return feature.Use();
	}
}

/// <summary>Estado da requisição: um nonce só, mesmo quando o pipeline é reexecutado (página de erro).</summary>
internal sealed class SeccoCspNonceFeature(HttpContext context)
{
	private const int NonceBytes = 16;

	/// <summary>128 bits novos por requisição, nunca derivados de nada que venha nela.</summary>
	internal string Value { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(NonceBytes));

	internal bool Used { get; private set; }

	internal string Use()
	{
		if (!Used)
		{
			Used = true;

			// Nonce reaproveitado entre respostas seria valor que o atacante lê e usa: nada de output cache.
			if (context.Features.Get<IOutputCacheFeature>() is { } outputCache)
			{
				outputCache.Context.AllowCacheStorage = false;
			}
		}

		return Value;
	}
}

internal sealed class SeccoCspNonce(IHttpContextAccessor accessor) : ISeccoCspNonce
{
	public string Value => (accessor.HttpContext
		?? throw new InvalidOperationException("O nonce da CSP só existe durante uma requisição HTTP."))
		.GetSeccoCspNonce();
}
