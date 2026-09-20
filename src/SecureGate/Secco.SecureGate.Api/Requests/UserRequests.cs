namespace Secco.SecureGate.Api.Requests;

/// <summary>Payload de criação de usuário (provisionamento por administrador, Fase 6.5).</summary>
/// <param name="Email">E-mail (também o username). Obrigatório.</param>
/// <param name="LocalLogin">
/// <c>true</c> (padrão) envia convite para a pessoa definir a própria senha; <c>false</c> cria
/// conta que entra só pelo diretório corporativo. O admin nunca define senha (ADR-0033).
/// </param>
/// <param name="Roles">Roles a atribuir no tenant (opcional).</param>
public sealed record CreateUserRequest(string? Email, bool? LocalLogin, IReadOnlyList<string>? Roles);

/// <summary>Payload do liga/desliga do login local (ADR-0033).</summary>
/// <param name="Enabled">
/// <c>true</c> religa a senha local e envia convite; <c>false</c> apaga a senha, encerra as
/// sessões e deixa a conta só com o diretório corporativo.
/// </param>
public sealed record SetLocalLoginRequest(bool Enabled);
