namespace Secco.NotificationHub.Infrastructure.Email;

/// <summary>Configuração do provider SMTP (seção <c>NotificationHub:Email</c>).</summary>
public sealed class NotificationHubEmailOptions
{
    /// <summary>Host do servidor SMTP.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Porta do servidor SMTP (default 587, STARTTLS).</summary>
    public int Port { get; set; } = 587;

    /// <summary>Usa STARTTLS (default true — nunca enviar credenciais em texto claro).</summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>Usuário de autenticação, quando o provider exigir.</summary>
    public string? Username { get; set; }

    /// <summary>Senha de autenticação — nunca logada (ADR-0020).</summary>
    public string? Password { get; set; }

    /// <summary>Endereço de remetente.</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>Nome de exibição do remetente, quando houver.</summary>
    public string? FromName { get; set; }
}
