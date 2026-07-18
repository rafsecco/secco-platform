using Secco.NotificationHub.Infrastructure.Email;

namespace Secco.NotificationHub.Tests.Integration;

/// <summary>
/// Substitui o envio real de e-mail nos testes de integração (sem SMTP real, ADR-0012):
/// o destinatário decide o comportamento — permite exercitar o job de envio real
/// (Hangfire + Testcontainers) sem depender de infraestrutura de e-mail externa.
/// </summary>
internal sealed class FakeEmailSender : IEmailSender
{
    /// <summary>Destinatário que sempre falha no envio — para testar o caminho de erro.</summary>
    public const string AlwaysFailingRecipient = "fail-always@notificationhub.test";

    public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken) =>
        recipient == AlwaysFailingRecipient
            ? throw new InvalidOperationException("Falha de envio simulada pelo teste.")
            : Task.CompletedTask;
}
