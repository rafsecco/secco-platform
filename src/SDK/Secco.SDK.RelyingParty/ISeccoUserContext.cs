using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Secco.SDK.RelyingParty;

/// <summary>
/// De onde o provedor tira a pessoa atual e como manda para novo login. O padrão usa o <c>HttpContext</c>;
/// um circuito de Blazor Server (sem <c>HttpContext</c> depois da primeira renderização) registra a própria
/// implementação, sobre o <c>AuthenticationStateProvider</c> — registre-a ANTES de chamar
/// <c>AddSeccoRelyingPartyTokens</c> (o registro padrão é <c>TryAdd</c>).
/// </summary>
public interface ISeccoUserContext
{
	/// <summary>Pessoa autenticada da requisição ou circuito atual, ou <c>null</c>.</summary>
	Task<ClaimsPrincipal?> GetUserAsync();

	/// <summary>
	/// Sinaliza que a sessão se perdeu e a pessoa precisa de novo login. Chamado depois de o cofre ser limpo;
	/// o padrão não faz nada, porque a próxima requisição passa pela validação do cookie, que rejeita a
	/// sessão sem cofre e leva ao login.
	/// </summary>
	void RequestReauthentication();
}

internal sealed class HttpContextSeccoUserContext(IHttpContextAccessor accessor) : ISeccoUserContext
{
	public Task<ClaimsPrincipal?> GetUserAsync() => Task.FromResult(accessor.HttpContext?.User);

	public void RequestReauthentication()
	{
	}
}
