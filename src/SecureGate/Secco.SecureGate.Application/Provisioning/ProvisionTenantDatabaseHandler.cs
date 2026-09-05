using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Provisioning;

/// <summary>Comando de provisionamento do banco de um tenant em um produto.</summary>
/// <param name="TenantId">Tenant dono do banco.</param>
/// <param name="Product">Identificador do produto (kebab-case).</param>
/// <param name="Target">Alvo declarado em configuração; vazio usa o alvo padrão.</param>
/// <param name="Server">Servidor de destino, quando nenhum alvo está configurado.</param>
/// <param name="CreateDatabase">
/// <c>true</c> cria o database; <c>false</c> assume um banco já existente e só cria o usuário de
/// aplicação com o mínimo necessário nele.
/// </param>
/// <param name="DatabaseName">Nome do database; vazio deriva de produto + slug do tenant.</param>
/// <param name="LoginName">Nome do login de aplicação; vazio deriva do nome do database.</param>
public sealed record ProvisionTenantDatabaseCommand(
	Guid TenantId,
	string? Product,
	string? Target,
	string? Server,
	bool CreateDatabase,
	string? DatabaseName,
	string? LoginName);

/// <summary>Resultado do provisionamento.</summary>
/// <param name="Applied">
/// <c>true</c> quando o SecureGate executou; <c>false</c> quando devolveu o script para aplicação
/// manual.
/// </param>
/// <param name="DatabaseName">Nome do database provisionado.</param>
/// <param name="LoginName">Nome do usuário de aplicação criado.</param>
/// <param name="Script">
/// SQL a aplicar, presente <b>somente</b> quando <paramref name="Applied"/> é <c>false</c>. Contém
/// a senha gerada, por necessidade de quem aplica — é devolvido uma única vez e não é recuperável.
/// </param>
public sealed record TenantDatabaseProvisioningDto(
	bool Applied,
	string DatabaseName,
	string LoginName,
	string? Script);

/// <summary>
/// Provisiona o banco dedicado de um tenant: cria (ou assume) o database, cria o usuário de
/// aplicação com o mínimo necessário e grava a connection string cifrada no catálogo.
/// </summary>
/// <remarks>
/// <b>Execução síncrona por decisão.</b> Considerado e descartado enfileirar num job: a credencial
/// privilegiada fica no mesmo processo de qualquer forma — a assincronia moveria <i>quando</i> ela
/// é usada, não <i>onde</i> mora. O que protege é a automação ser opt-in, o endpoint exigir
/// <c>securegate:admin</c> e a conexão privilegiada existir só nesta operação. Criar database leva
/// segundos, e repetir DDL automaticamente é mais perigoso que útil.
/// <para>
/// Em modo script a connection string é gravada <b>antes</b> de o banco existir. É intencional:
/// a alternativa obrigaria o operador a cadastrá-la depois, à mão, que é justamente o passo que a
/// issue #3 quer eliminar. O painel de status é quem torna esse intervalo visível.
/// </para>
/// </remarks>
public sealed class ProvisionTenantDatabaseHandler(
	ITenantRepository repository,
	TenantDatabaseProvisioningOptions options,
	IEnumerable<ITenantDatabaseProvisioner> provisioners)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando de provisionamento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<TenantDatabaseProvisioningDto>> HandleAsync(
		ProvisionTenantDatabaseCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var product = command.Product?.Trim().ToLowerInvariant() ?? string.Empty;

		if (!TenantInputRules.IsValidSlug(product, TenantDatabase.ProductMaxLength))
		{
			return Result.Failure<TenantDatabaseProvisioningDto>(SecureGateErrors.TenantDatabases.ProductInvalid);
		}

		var tenant = await repository.GetByIdAsync(command.TenantId, cancellationToken).ConfigureAwait(false);

		if (tenant is null)
		{
			return Result.Failure<TenantDatabaseProvisioningDto>(SecureGateErrors.Tenants.NotFound);
		}

		// Reprovisionar por cima do que existe apagaria o acesso vigente sem aviso; rotação de
		// credencial é operação própria e ainda não existe (registrada fora de escopo).
		var existing = await repository.GetDatabaseAsync(command.TenantId, product, cancellationToken)
			.ConfigureAwait(false);

		if (existing is not null)
		{
			return Result.Failure<TenantDatabaseProvisioningDto>(
				SecureGateErrors.TenantDatabases.AlreadyProvisioned);
		}

		var targetResult = ResolveTarget(command);

		if (targetResult.IsFailure)
		{
			return Result.Failure<TenantDatabaseProvisioningDto>(targetResult.Error);
		}

		var (target, server, provisioner) = targetResult.Value;

		var databaseName = string.IsNullOrWhiteSpace(command.DatabaseName)
			? DatabaseIdentifierPolicy.Derive("secco", product, tenant.Slug)
			: command.DatabaseName.Trim().ToLowerInvariant();

		var loginName = string.IsNullOrWhiteSpace(command.LoginName)
			? DatabaseIdentifierPolicy.Derive(databaseName, "app")
			: command.LoginName.Trim().ToLowerInvariant();

		if (!DatabaseIdentifierPolicy.IsValid(databaseName) || !DatabaseIdentifierPolicy.IsValid(loginName))
		{
			return Result.Failure<TenantDatabaseProvisioningDto>(
				SecureGateErrors.TenantDatabases.ProvisioningIdentifierInvalid);
		}

		var plan = new TenantDatabaseProvisioningPlan(
			server,
			databaseName,
			loginName,
			TenantDatabaseSecretGenerator.GeneratePassword(),
			command.CreateDatabase);

		var connectionString = provisioner.BuildTenantConnectionString(plan);

		if (connectionString.Length > TenantDatabase.ConnectionStringMaxLength)
		{
			return Result.Failure<TenantDatabaseProvisioningDto>(
				SecureGateErrors.TenantDatabases.ConnectionStringTooLong);
		}

		var applied = false;

		if (target is { CanExecute: true })
		{
			var execution = await provisioner
				.ExecuteAsync(plan, target.AdminConnectionString!, cancellationToken)
				.ConfigureAwait(false);

			if (!execution.Succeeded)
			{
				return Result.Failure<TenantDatabaseProvisioningDto>(
					SecureGateErrors.TenantDatabases.ProvisioningFailed(execution.FailureReason!));
			}

			applied = true;
		}

		await repository
			.AddDatabaseAsync(new TenantDatabase(command.TenantId, product, connectionString), cancellationToken)
			.ConfigureAwait(false);

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return Result.Success(new TenantDatabaseProvisioningDto(
			applied,
			databaseName,
			loginName,
			applied ? null : provisioner.BuildScript(plan)));
	}

	/// <summary>Resolve alvo, servidor e provisionador, ou o erro que impede o provisionamento.</summary>
	/// <param name="command">Comando de provisionamento.</param>
	private Result<(ProvisioningTarget? Target, string Server, ITenantDatabaseProvisioner Provisioner)> ResolveTarget(
		ProvisionTenantDatabaseCommand command)
	{
		var target = options.Resolve(command.Target);

		if (target is null && !string.IsNullOrWhiteSpace(command.Target))
		{
			return Result.Failure<(ProvisioningTarget?, string, ITenantDatabaseProvisioner)>(
				SecureGateErrors.TenantDatabases.ProvisioningTargetUnknown);
		}

		var server = target?.Server ?? command.Server?.Trim();

		if (string.IsNullOrWhiteSpace(server))
		{
			return Result.Failure<(ProvisioningTarget?, string, ITenantDatabaseProvisioner)>(
				SecureGateErrors.TenantDatabases.ProvisioningServerRequired);
		}

		var providerName = target?.Provider ?? SecureGateDatabaseProviderNames.SqlServer;

		var provisioner = provisioners.FirstOrDefault(candidate =>
			string.Equals(candidate.Provider, providerName, StringComparison.OrdinalIgnoreCase));

		return provisioner is null
			? Result.Failure<(ProvisioningTarget?, string, ITenantDatabaseProvisioner)>(
				SecureGateErrors.TenantDatabases.ProvisioningProviderUnsupported)
			: Result.Success<(ProvisioningTarget?, string, ITenantDatabaseProvisioner)>((target, server, provisioner));
	}
}

/// <summary>Nomes de provider aceitos no provisionamento (ADR-0018).</summary>
public static class SecureGateDatabaseProviderNames
{
	/// <summary>Provider padrão da plataforma.</summary>
	public const string SqlServer = "SqlServer";

	/// <summary>Segundo provider — entra na rodada seguinte, com a mesma abstração.</summary>
	public const string Postgres = "Postgres";
}
