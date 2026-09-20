namespace Secco.SecureGate.Api.Requests;

/// <summary>Payload de criação de usuário (provisionamento por administrador, Fase 6.5).</summary>
/// <param name="Email">E-mail (também o username). Obrigatório.</param>
/// <param name="LocalLogin">
/// <c>true</c> (padrão) envia convite para a pessoa definir a própria senha; <c>false</c> cria
/// conta que entra só pelo diretório corporativo. O admin nunca define senha (ADR-0033).
/// </param>
/// <param name="Roles">Roles a atribuir no tenant (opcional).</param>
public sealed record CreateUserRequest(string? Email, bool? LocalLogin, IReadOnlyList<string>? Roles);
