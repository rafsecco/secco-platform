using System.Security.Cryptography.X509Certificates;

namespace Secco.SecureGate.Api.Extensions;

/// <summary>
/// Origem do certificado de assinatura do servidor OIDC. Fora de Development e Testing ele é
/// obrigatório (fail-fast, ADR-0020): uma chave gerada dentro do contêiner morre com ele e
/// invalida todo token emitido. Entregue como arquivo (<c>CertificatePath</c>) ou como PKCS#12
/// em base64 (<c>CertificateBase64</c>), nunca os dois.
/// </summary>
internal static class SigningCertificateSource
{
	private const string PathKey = "SecureGate:Signing:CertificatePath";
	private const string Base64Key = "SecureGate:Signing:CertificateBase64";
	private const string PasswordKey = "SecureGate:Signing:CertificatePassword";

	/// <summary>
	/// Carrega o certificado configurado, ou <c>null</c> em Development e Testing (onde o
	/// OpenIddict usa chaves de desenvolvimento/efêmeras). As mensagens citam a chave de
	/// configuração, nunca o valor.
	/// </summary>
	/// <param name="configuration">Configuração do host.</param>
	/// <param name="environment">Ambiente de hospedagem.</param>
	internal static X509Certificate2? Load(IConfiguration configuration, IHostEnvironment environment)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(environment);

		if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
		{
			return null;
		}

		var path = configuration[PathKey];
		var base64 = configuration[Base64Key];
		var password = configuration[PasswordKey];
		var hasPath = !string.IsNullOrWhiteSpace(path);
		var hasBase64 = !string.IsNullOrWhiteSpace(base64);

		if (hasPath && hasBase64)
		{
			throw new InvalidOperationException(
				$"'{PathKey}' e '{Base64Key}' foram informados juntos — use apenas um (fail-fast, ADR-0020).");
		}

		if (!hasPath && !hasBase64)
		{
			throw new InvalidOperationException(
				$"'{PathKey}' (ou '{Base64Key}') é obrigatório fora de Development e Testing — o SecureGate não sobe sem certificado explícito (fail-fast, ADR-0020).");
		}

		X509Certificate2 certificate;
		try
		{
			if (hasPath)
			{
				certificate = X509CertificateLoader.LoadPkcs12FromFile(path!, password);
			}
			else
			{
				byte[] bytes;
				try
				{
					bytes = Convert.FromBase64String(base64!);
				}
				catch (FormatException ex)
				{
					throw new InvalidOperationException(
						$"'{Base64Key}' não é base64 válido (fail-fast, ADR-0020).", ex);
				}

				certificate = X509CertificateLoader.LoadPkcs12(bytes, password);
			}
		}
		catch (Exception ex) when (ex is not InvalidOperationException)
		{
			throw new InvalidOperationException(
				$"Não foi possível abrir o certificado PKCS#12 de '{(hasPath ? PathKey : Base64Key)}' — confira o conteúdo e '{PasswordKey}' (fail-fast, ADR-0020).", ex);
		}

		if (!certificate.HasPrivateKey)
		{
			certificate.Dispose();
			throw new InvalidOperationException(
				$"O certificado de '{(hasPath ? PathKey : Base64Key)}' não tem chave privada — é necessária para assinar tokens (fail-fast, ADR-0020).");
		}

		return certificate;
	}
}
