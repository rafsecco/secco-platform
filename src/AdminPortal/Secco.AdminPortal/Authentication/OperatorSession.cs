namespace Secco.AdminPortal.Authentication;

/// <summary>Tokens do operador guardados no servidor — nunca no cookie (ADR-0032).</summary>
/// <param name="AccessToken">Access token atual.</param>
/// <param name="RefreshToken">Refresh token atual (rotativo).</param>
/// <param name="ExpiresAt">Vencimento do access token.</param>
public sealed record OperatorSession(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
