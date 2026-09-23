using Microsoft.AspNetCore.Identity;
using QRCoder;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Infrastructure.Credentials;

/// <summary>
/// Adaptador do cadastro de 2FA sobre <see cref="UserManager{TUser}"/> (entrega D). Nenhum método
/// aqui lança para fluxo de negócio: ausência de usuário devolve <c>null</c>/vazio (ADR-0004).
/// </summary>
internal sealed class IdentityTwoFactorSetup(UserManager<User> userManager, CredentialOptions options) : ITwoFactorSetup
{
	private const int RecoveryCodeCount = 10;

	/// <summary>Provedor do autenticador, como o Identity o nomeia internamente.</summary>
	private const string AuthenticatorProvider = "[AspNetUserStore]";

	/// <summary>Nome do token da chave — constante interna do Identity, replicada aqui.</summary>
	private const string AuthenticatorKeyTokenName = "AuthenticatorKey";

	/// <summary>Nome do token dos códigos de recuperação — idem.</summary>
	private const string RecoveryCodeTokenName = "RecoveryCodes";

	/// <inheritdoc />
	public async Task<TwoFactorState?> GetStateAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		if (await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false) is not { } user)
		{
			return null;
		}

		return new TwoFactorState(
			user.TwoFactorEnabled,
			await userManager.GetAuthenticatorKeyAsync(user).ConfigureAwait(false) is not null,
			await userManager.CountRecoveryCodesAsync(user).ConfigureAwait(false));
	}

	/// <inheritdoc />
	public async Task<TwoFactorEnrollment?> StartEnrollmentAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		if (await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false) is not { } user)
		{
			return null;
		}

		// Chave nova a cada início: recomeçar o cadastro invalida o QR anterior.
		await userManager.ResetAuthenticatorKeyAsync(user).ConfigureAwait(false);
		var key = await userManager.GetAuthenticatorKeyAsync(user).ConfigureAwait(false);

		return new TwoFactorEnrollment(FormatKey(key!), BuildQrCode(user.Email!, key!));
	}

	/// <inheritdoc />
	public async Task<bool> ConfirmAsync(Guid userId, string code, CancellationToken cancellationToken = default)
	{
		if (await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false) is not { } user)
		{
			return false;
		}

		var valid = await userManager.VerifyTwoFactorTokenAsync(
			user, TokenOptions.DefaultAuthenticatorProvider, (code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal))
			.ConfigureAwait(false);

		if (!valid)
		{
			return false;
		}

		return (await userManager.SetTwoFactorEnabledAsync(user, true).ConfigureAwait(false)).Succeeded;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		if (await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false) is not { } user)
		{
			return [];
		}

		// O Identity guarda só o hash; estes valores existem apenas nesta resposta.
		var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount).ConfigureAwait(false);

		return [.. codes ?? []];
	}

	/// <inheritdoc />
	public async Task DisableAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		if (await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false) is not { } user)
		{
			return;
		}

		await userManager.SetTwoFactorEnabledAsync(user, false).ConfigureAwait(false);

		// APAGAR, não regerar: o ResetAuthenticatorKeyAsync do Identity grava uma chave NOVA, e a
		// conta continuaria "com autenticador cadastrado" — só que com um segredo que ninguém
		// conhece. A spec da entrega D exige voltar ao estado "sem 2FA cadastrado", e é isso que
		// faz o reset do admin zerar em vez de isentar.
		await userManager.RemoveAuthenticationTokenAsync(user, AuthenticatorProvider, AuthenticatorKeyTokenName)
			.ConfigureAwait(false);
		await userManager.RemoveAuthenticationTokenAsync(user, AuthenticatorProvider, RecoveryCodeTokenName)
			.ConfigureAwait(false);
	}

	/// <summary>Chave em blocos de 4, minúscula — o formato que se digita sem errar.</summary>
	private static string FormatKey(string key)
	{
		var lower = key.ToLowerInvariant();
		var blocks = Enumerable.Range(0, (lower.Length + 3) / 4)
			.Select(i => lower.Substring(i * 4, Math.Min(4, lower.Length - (i * 4))));

		return string.Join(' ', blocks);
	}

	/// <summary>QR gerado aqui dentro: o segredo não passa por serviço de terceiro (ADR-0020).</summary>
	private string BuildQrCode(string email, string key)
	{
		var issuer = Uri.EscapeDataString(options.TwoFactorIssuer);
		var uri = $"otpauth://totp/{issuer}:{Uri.EscapeDataString(email)}?secret={key}&issuer={issuer}&digits=6";

		using var generator = new QRCodeGenerator();
		using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
		using var png = new PngByteQRCode(data);

		return "data:image/png;base64," + Convert.ToBase64String(png.GetGraphic(6));
	}
}
