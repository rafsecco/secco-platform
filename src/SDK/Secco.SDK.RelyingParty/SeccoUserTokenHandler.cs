using System.Net.Http.Headers;

namespace Secco.SDK.RelyingParty;

/// <summary>
/// Anexa <c>Authorization: Bearer &lt;token da pessoa&gt;</c> a cada requisição (ADR-0046). Sem sessão, a
/// requisição segue sem o header e a API responde 401. Um <c>Authorization</c> já definido pelo chamador
/// é respeitado.
/// </summary>
/// <remarks>
/// O handler resolve a pessoa pelo <see cref="ISeccoUserTokenProvider"/> do escopo em que o <c>HttpClient</c> foi
/// criado. O <c>IHttpClientFactory</c> cria o handler no escopo do próprio <i>handler</i>, não no da requisição:
/// isso funciona com o <c>HttpContext</c> (ambiente), mas num circuito de Blazor Server o provedor precisa do
/// escopo do circuito — ali, obtenha o token do <see cref="ISeccoUserTokenProvider"/> e anexe-o ao cliente.
/// </remarks>
public sealed class SeccoUserTokenHandler(ISeccoUserTokenProvider tokenProvider) : DelegatingHandler
{
	/// <inheritdoc />
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Headers.Authorization is null
			&& await tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false) is { Length: > 0 } token)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}

		return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
	}
}
