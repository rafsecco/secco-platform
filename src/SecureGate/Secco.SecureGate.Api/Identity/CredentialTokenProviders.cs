using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Api.Identity;

/// <summary>Nomes dos provedores de token de credencial (ADR-0033).</summary>
public static class CredentialTokenProviders
{
	/// <summary>Convite — validade longa (padrão 72 h).</summary>
	public const string Invite = "SeccoInvite";

	/// <summary>Redefinição de senha — validade curta (padrão 30 min).</summary>
	public const string Reset = "SeccoReset";

	/// <summary>Propósito do token de convite, dentro do provedor.</summary>
	public const string InvitePurpose = "secco-invite";

	/// <summary>Propósito do token de redefinição, dentro do provedor.</summary>
	public const string ResetPurpose = "secco-reset";

	/// <summary>
	/// Troca de e-mail. O propósito não é nosso: quem o monta é o próprio Identity, como
	/// <c>ChangeEmail:{novo}</c> — e é isso que prende o token ao endereço de destino.
	/// </summary>
	public const string EmailChange = "SeccoEmailChange";
}

/// <summary>
/// Validade do token de convite. Existe como tipo próprio porque o provedor do Identity lê a
/// validade de <c>IOptions&lt;T&gt;</c>: sem um T por provedor, convite e redefinição
/// compartilhariam a mesma janela — e 72 horas é validade demais para um link de redefinição.
/// </summary>
public sealed class InviteTokenProviderOptions : DataProtectionTokenProviderOptions;

/// <summary>Validade do token de redefinição (ver <see cref="InviteTokenProviderOptions"/>).</summary>
public sealed class ResetTokenProviderOptions : DataProtectionTokenProviderOptions;

/// <summary>Provedor do convite — só existe para carregar as próprias options.</summary>
/// <param name="dataProtectionProvider">Provedor de Data Protection (chaves no banco, ADR-0033).</param>
/// <param name="options">Validade do convite.</param>
/// <param name="logger">Log do provedor base.</param>
public sealed class InviteTokenProvider(
	IDataProtectionProvider dataProtectionProvider,
	IOptions<InviteTokenProviderOptions> options,
	ILogger<DataProtectorTokenProvider<User>> logger)
	: DataProtectorTokenProvider<User>(dataProtectionProvider, options, logger);

/// <summary>Validade do token de troca de e-mail (ver <see cref="InviteTokenProviderOptions"/>).</summary>
public sealed class EmailChangeTokenProviderOptions : DataProtectionTokenProviderOptions;

/// <summary>Provedor da redefinição — idem.</summary>
/// <param name="dataProtectionProvider">Provedor de Data Protection.</param>
/// <param name="options">Validade da redefinição.</param>
/// <param name="logger">Log do provedor base.</param>
public sealed class ResetTokenProvider(
	IDataProtectionProvider dataProtectionProvider,
	IOptions<ResetTokenProviderOptions> options,
	ILogger<DataProtectorTokenProvider<User>> logger)
	: DataProtectorTokenProvider<User>(dataProtectionProvider, options, logger);

/// <summary>Provedor da troca de e-mail — idem.</summary>
/// <param name="dataProtectionProvider">Provedor de Data Protection.</param>
/// <param name="options">Validade da troca de e-mail.</param>
/// <param name="logger">Log do provedor base.</param>
public sealed class EmailChangeTokenProvider(
	IDataProtectionProvider dataProtectionProvider,
	IOptions<EmailChangeTokenProviderOptions> options,
	ILogger<DataProtectorTokenProvider<User>> logger)
	: DataProtectorTokenProvider<User>(dataProtectionProvider, options, logger);
