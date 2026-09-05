using Microsoft.EntityFrameworkCore;
using Secco.NotificationHub.Domain.Channels;
using Secco.NotificationHub.Domain.InAppNotifications;
using Secco.NotificationHub.Domain.Notifications;
using Secco.SDK.EntityFrameworkCore;
using Secco.SDK.EntityFrameworkCore.Cryptography;

namespace Secco.NotificationHub.Infrastructure.Contexts;

/// <summary>
/// Contexto de dados sobre o banco do tenant atual (ADR-0005) — a connection string vem
/// do <c>ITenantConnectionFactory</c> a cada requisição. Herda de <see cref="SeccoDbContext"/>:
/// nomenclatura da ADR-0017 aplicada por convention — ninguém digita nomes de coluna.
/// </summary>
public sealed class NotificationHubDbContext(
	DbContextOptions<NotificationHubDbContext> options,
	ISeccoSecretCipher? secretCipher = null)
	: SeccoDbContext(options)
{
	private readonly ISeccoSecretCipher? _secretCipher = secretCipher;

	/// <summary>Entregas rastreadas por canal externo (tabela <c>tb_notifications</c>).</summary>
	public DbSet<Notification> Notifications => Set<Notification>();

	/// <summary>Destino configurado dos canais externos (tabela <c>tb_channel_configurations</c>, ADR-0029).</summary>
	public DbSet<ChannelConfiguration> ChannelConfigurations => Set<ChannelConfiguration>();

	/// <summary>Inbox in-app (tabela <c>tb_in_app_notifications</c>, Fase 8.4).</summary>
	public DbSet<InAppNotification> InAppNotifications => Set<InAppNotification>();

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationHubDbContext).Assembly);

		// Cifragem em repouso do destino (ADR-0025/0029). O cipher é opcional para que as
		// ferramentas de design-time do EF (migrations) construam o contexto sem container;
		// no runtime ele é sempre injetado. O domínio segue puro: quem cifra é o converter.
		if (_secretCipher is { } cipher)
		{
			modelBuilder.Entity<ChannelConfiguration>()
				.Property(configuration => configuration.Destination)
				.HasConversion(
					plaintext => cipher.Encrypt(plaintext),
					stored => cipher.Decrypt(stored));
		}
	}
}
