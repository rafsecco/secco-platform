namespace Secco.NotificationHub.Domain.Notifications;

/// <summary>
/// Canal de uma entrega rastreada (coluna <c>ie_channel</c>, ADR-0029).
/// </summary>
/// <remarks>
/// <c>Email = 0</c> não é acaso: é o valor que a migration atribui às linhas que já existiam
/// quando a coluna nasceu. Toda notificação anterior à ADR-0029 era, por construção, de e-mail —
/// e o histórico precisa continuar significando o que significava.
/// <para>
/// O inbox in-app NÃO entra aqui: ele tem entidade própria porque o ciclo de vida é outro
/// (lido/não lido, sem entrega nem retry).
/// </para>
/// </remarks>
public enum NotificationChannel
{
	/// <summary>Envio por e-mail, com destinatário no próprio registro.</summary>
	Email = 0,

	/// <summary>Mensagem em canal do Microsoft Teams; destino vem da configuração do tenant.</summary>
	Teams = 1,

	/// <summary>Mensagem em canal do Slack; destino vem da configuração do tenant.</summary>
	Slack = 2,
}
