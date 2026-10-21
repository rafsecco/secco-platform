namespace Secco.SDK.RelyingParty;

/// <summary>
/// Token curto obtido por troca RFC 8693 (ADR-0050), guardado no cofre da sessão. O <c>ToString</c> do
/// record é suprimido para o token não vazar por log ou mensagem de exceção (ADR-0020).
/// </summary>
/// <param name="AccessToken">Access token trocado.</param>
/// <param name="ExpiresAt">Vencimento do token.</param>
public sealed record SeccoExchangedToken(string AccessToken, DateTimeOffset ExpiresAt)
{
	/// <inheritdoc />
	public override string ToString() => nameof(SeccoExchangedToken);
}

/// <summary>Quando e como a pessoa se autenticou, lido do access token da sessão (ADR-0050).</summary>
/// <param name="AuthenticatedAt">Instante da autenticação (<c>auth_time</c>); <c>null</c> se desconhecido.</param>
/// <param name="Methods">Métodos de autenticação (<c>amr</c>); vazio se desconhecidos.</param>
public sealed record SeccoAuthentication(DateTimeOffset? AuthenticatedAt, IReadOnlyList<string> Methods)
{
	/// <summary>Se a autenticação usou o método (comparação ordinal), por exemplo <c>mfa</c>.</summary>
	/// <param name="method">Método a procurar.</param>
	public bool HasMethod(string method) => Methods.Contains(method, StringComparer.Ordinal);
}

/// <summary>Estado de um pedido de token trocado.</summary>
public enum SeccoExchangeStatus
{
	/// <summary>O token foi emitido (ou veio do cofre, ainda válido).</summary>
	Issued,

	/// <summary>O emissor recusou a troca. A recusa é genérica de propósito (ADR-0031, ADR-0049).</summary>
	Refused,

	/// <summary>Não há sessão da pessoa (anônimo, ou sessão perdida — neste caso a pessoa foi mandada para novo login).</summary>
	NoSession,
}

/// <summary>
/// Resultado de <see cref="ISeccoExchangedTokenProvider.GetAsync"/>. A recusa não diz o motivo: a plataforma
/// responde igual a qualquer causa (sem designação, sem segundo fator, autenticação antiga, conta bloqueada…)
/// para não servir de oráculo (ADR-0031, ADR-0049) — quem decide pedir reautenticação é a aplicação.
/// </summary>
public sealed class SeccoExchangeResult
{
	private SeccoExchangeResult(SeccoExchangeStatus status, string? accessToken, DateTimeOffset? expiresAt)
	{
		Status = status;
		AccessToken = accessToken;
		ExpiresAt = expiresAt;
	}

	/// <summary>Estado do pedido.</summary>
	public SeccoExchangeStatus Status { get; }

	/// <summary>Token trocado quando <see cref="Status"/> é <see cref="SeccoExchangeStatus.Issued"/>; senão <c>null</c>.</summary>
	public string? AccessToken { get; }

	/// <summary>Vencimento do token quando emitido; senão <c>null</c>.</summary>
	public DateTimeOffset? ExpiresAt { get; }

	/// <summary>Se o token foi emitido.</summary>
	public bool IsIssued => Status == SeccoExchangeStatus.Issued;

	/// <summary>Token emitido.</summary>
	/// <param name="accessToken">Access token trocado.</param>
	/// <param name="expiresAt">Vencimento.</param>
	public static SeccoExchangeResult Issued(string accessToken, DateTimeOffset expiresAt) =>
		new(SeccoExchangeStatus.Issued, accessToken, expiresAt);

	/// <summary>Troca recusada (genérica).</summary>
	public static SeccoExchangeResult Refused() => new(SeccoExchangeStatus.Refused, null, null);

	/// <summary>Sem sessão.</summary>
	public static SeccoExchangeResult NoSession() => new(SeccoExchangeStatus.NoSession, null, null);

	/// <inheritdoc />
	public override string ToString() => $"{nameof(SeccoExchangeResult)}({Status})";
}

/// <summary>
/// A troca não produziu token e a aplicação precisa pedir nova autenticação à pessoa (ADR-0050). O motivo é
/// genérico de propósito; trate redirecionando, por exemplo, para um desafio com <c>prompt=login</c>.
/// </summary>
public sealed class SeccoReauthenticationRequiredException : Exception
{
	/// <summary>Cria a exceção.</summary>
	/// <param name="scope">Escopo pedido.</param>
	/// <param name="status">Estado do pedido (nunca <see cref="SeccoExchangeStatus.Issued"/>).</param>
	public SeccoReauthenticationRequiredException(string scope, SeccoExchangeStatus status)
		: base($"Não foi possível obter o token trocado para o escopo '{scope}' ({status}); a pessoa precisa se autenticar de novo.")
	{
		Scope = scope;
		Status = status;
	}

	/// <summary>Escopo pedido.</summary>
	public string Scope { get; }

	/// <summary>Estado do pedido.</summary>
	public SeccoExchangeStatus Status { get; }
}
