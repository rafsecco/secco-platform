using System.Net.Http.Headers;

namespace Secco.SDK.RelyingParty;

/// <summary>
/// Anexa <c>Authorization: Bearer &lt;token trocado do escopo&gt;</c> a cada requisição (ADR-0050). Quando não há
/// token (troca recusada ou sem sessão), lança <see cref="SeccoReauthenticationRequiredException"/> — nunca segue
/// sem autorização nem recai no token comum da pessoa, que não tem o privilégio do escopo. Um <c>Authorization</c>
/// já definido é SUBSTITUÍDO: este handler existe para que a chamada leve o token trocado, e não outro.
/// </summary>
/// <remarks>
/// As mesmas ressalvas de escopo do <see cref="SeccoUserTokenHandler"/> valem: o <c>IHttpClientFactory</c> cria o
/// handler no escopo do próprio <i>handler</i>, não no da requisição — isso funciona com o <c>HttpContext</c>
/// (ambiente), mas num circuito de Blazor Server obtenha o token do <see cref="ISeccoExchangedTokenProvider"/>
/// no escopo do circuito e anexe-o ao cliente. A aplicação trata a exceção redirecionando a pessoa.
/// </remarks>
/// <param name="tokenProvider">Provedor do token trocado.</param>
/// <param name="scope">Escopo trocado para este client.</param>
public sealed class SeccoExchangedTokenHandler(ISeccoExchangedTokenProvider tokenProvider, string scope) : DelegatingHandler
{
	/// <inheritdoc />
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var result = await tokenProvider.GetAsync(scope, cancellationToken).ConfigureAwait(false);

		if (result is not { Status: SeccoExchangeStatus.Issued, AccessToken: { Length: > 0 } token })
		{
			throw new SeccoReauthenticationRequiredException(scope, result.Status == SeccoExchangeStatus.Issued ? SeccoExchangeStatus.Refused : result.Status);
		}

		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

		return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
	}
}
