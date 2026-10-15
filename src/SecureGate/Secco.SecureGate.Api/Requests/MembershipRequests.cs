namespace Secco.SecureGate.Api.Requests;

/// <summary>Payload de inclusão de membro por e-mail (ADR-0040).</summary>
/// <param name="Email">E-mail de uma conta que já existe na instalação. Obrigatório.</param>
public sealed record AddTenantMemberRequest(string? Email);
