using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.NotificationHub.Domain.Channels;

namespace Secco.NotificationHub.Infrastructure.Mappings;

/// <summary>
/// Mapeamento da configuração de canal (ADR-0029). Nomes de coluna saem da convention da
/// ADR-0017 — nada é digitado à mão aqui.
/// </summary>
internal sealed class ChannelConfigurationConfiguration : IEntityTypeConfiguration<ChannelConfiguration>
{
	public void Configure(EntityTypeBuilder<ChannelConfiguration> builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		builder.Property(configuration => configuration.Channel)
			.IsRequired()
			.HasMaxLength(ChannelConfiguration.ChannelMaxLength);

		// O teto acomoda o ciphertext, que é maior que o texto em claro (nonce + tag + base64).
		builder.Property(configuration => configuration.Destination)
			.IsRequired()
			.HasMaxLength(ChannelConfiguration.DestinationMaxLength * 2);

		builder.Property(configuration => configuration.Enabled).IsRequired();
		builder.Property(configuration => configuration.CreatedAt).IsRequired();
		builder.Property(configuration => configuration.UpdatedAt).IsRequired();

		// Um destino por canal por tenant — o tenant é o próprio banco.
		builder.HasIndex(configuration => configuration.Channel).IsUnique();
	}
}
