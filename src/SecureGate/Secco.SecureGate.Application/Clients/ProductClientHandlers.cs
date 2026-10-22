using Secco.SecureGate.Application.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Clients;

/// <summary>Registra client de produto vinculado ao tenant (ADR-0037). Secret exibido uma única vez.</summary>
public sealed class CreateProductClientHandler(
	ITenantRepository tenants, ProductClientAccessValidator validator, IProductClientStore store)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="command">Comando.</param>
	/// <param name="byTenantAdmin">Pela autoridade de admin de tenant: só client de máquina (ADR-0048).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<CreatedProductClientDto>> HandleAsync(
		Guid tenantId, ProductClientCommand command, bool byTenantAdmin = false, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (byTenantAdmin && command.Type == ProductClientRules.AuthorizationCodeType)
		{
			return Result.Failure<CreatedProductClientDto>(SecureGateErrors.Clients.InteractiveRequiresOperator);
		}

		if (byTenantAdmin && ProductClientTenantAdminGuard.GrantsTenantManage(command.Scopes))
		{
			return Result.Failure<CreatedProductClientDto>(SecureGateErrors.Clients.TenantManageRequiresOperator);
		}

		if (await tenants.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false) is not { } tenant)
		{
			return Result.Failure<CreatedProductClientDto>(SecureGateErrors.Tenants.NotFound);
		}

		// Token sem tenant_id (operador) nunca nasce de client criado pela API: o interativo da
		// plataforma vem só da configuração (ADR-0037/ADR-0040)
		if (command.Type == ProductClientRules.AuthorizationCodeType && tenantId == SecureGatePlatform.TenantId)
		{
			return Result.Failure<CreatedProductClientDto>(SecureGateErrors.Clients.InteractiveInPlatformTenant);
		}

		if (!tenant.IsActive)
		{
			return Result.Failure<CreatedProductClientDto>(SecureGateErrors.Clients.TenantInactive);
		}

		var validation = await validator.ValidateAsync(tenantId, command, cancellationToken).ConfigureAwait(false);

		if (validation.IsFailure)
		{
			return Result.Failure<CreatedProductClientDto>(validation.Error);
		}

		var access = validation.Value;

		if (await store.NameExistsAsync(tenantId, access.Name, exceptClientId: null, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure<CreatedProductClientDto>(SecureGateErrors.Clients.NameAlreadyExists);
		}

		var secret = ProductClientRules.NewSecret();
		var created = await store.CreateAsync(tenantId, ProductClientRules.NewClientId(), secret, access, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success(new CreatedProductClientDto(
			created.ClientId, created.Name, created.Scopes, created.Roles, created.CreatedAt, secret,
			created.Type, created.RedirectUris, created.PostLogoutRedirectUris));
	}
}

/// <summary>Lista os clients de produto do tenant.</summary>
public sealed class ListProductClientsHandler(IProductClientStore store)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<IReadOnlyList<ProductClientDto>>> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Result.Success(await store.ListAsync(tenantId, cancellationToken).ConfigureAwait(false));
}

/// <summary>Detalha um client de produto do tenant.</summary>
public sealed class GetProductClientHandler(IProductClientStore store)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<ProductClientDto>> HandleAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default) =>
		await store.GetAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false) is { } client
			? Result.Success(client)
			: Result.Failure<ProductClientDto>(SecureGateErrors.Clients.NotFound);
}

/// <summary>Substitui nome, escopos e papéis — PUT idempotente de fato (ADR-0034). Não mexe no secret.</summary>
public sealed class UpdateProductClientHandler(ProductClientAccessValidator validator, IProductClientStore store)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="clientId">Client da rota.</param>
	/// <param name="command">Comando.</param>
	/// <param name="byTenantAdmin">Pela autoridade de admin de tenant: só client de máquina (ADR-0048).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(
		Guid tenantId, string clientId, ProductClientCommand command, bool byTenantAdmin = false,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var existing = await store.GetAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false);

		// Alterar um client de login (redirect URI) é tão perigoso quanto criá-lo (ADR-0048)
		if (byTenantAdmin
			&& (command.Type == ProductClientRules.AuthorizationCodeType
				|| existing?.Type == ProductClientRules.AuthorizationCodeType))
		{
			return Result.Failure(SecureGateErrors.Clients.InteractiveRequiresOperator);
		}

		// ADR-0052: nem acrescentar a gestão do tenant, nem mexer em client que já a tem
		if (byTenantAdmin
			&& (ProductClientTenantAdminGuard.GrantsTenantManage(command.Scopes)
				|| ProductClientTenantAdminGuard.GrantsTenantManage(existing?.Scopes)))
		{
			return Result.Failure(SecureGateErrors.Clients.TenantManageRequiresOperator);
		}

		// O tipo é imutável (ADR-0040): trocar máquina por interativo é outro client. Tipo omitido
		// vale client_credentials, então também não converte um interativo.
		if (existing is not null
			&& (command.Type ?? ProductClientRules.ClientCredentialsType) != (existing.Type ?? ProductClientRules.ClientCredentialsType))
		{
			return Result.Failure(SecureGateErrors.Clients.TypeImmutable);
		}

		var validation = await validator.ValidateAsync(tenantId, command, cancellationToken).ConfigureAwait(false);

		if (validation.IsFailure)
		{
			return Result.Failure(validation.Error);
		}

		if (await store.NameExistsAsync(tenantId, validation.Value.Name, clientId, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure(SecureGateErrors.Clients.NameAlreadyExists);
		}

		return await store.UpdateAsync(tenantId, clientId, validation.Value, cancellationToken).ConfigureAwait(false)
			? Result.Success()
			: Result.Failure(SecureGateErrors.Clients.NotFound);
	}
}

/// <summary>
/// Troca o secret na hora (ADR-0037): o antigo morre, tokens já emitidos valem até expirar. POST de
/// efeito — o SDK nunca o repete, então um timeout não gera secret perdido no meio (ADR-0034).
/// </summary>
public sealed class RotateProductClientSecretHandler(IProductClientStore store)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="clientId">Client da rota.</param>
	/// <param name="byTenantAdmin">Pela autoridade de admin de tenant: só client de máquina (ADR-0048).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<ProductClientSecretDto>> HandleAsync(
		Guid tenantId, string clientId, bool byTenantAdmin = false, CancellationToken cancellationToken = default)
	{
		if (byTenantAdmin
			&& await ProductClientTenantAdminGuard.OperatorOnlyAsync(store, tenantId, clientId, cancellationToken).ConfigureAwait(false)
				is { } operatorOnly)
		{
			return Result.Failure<ProductClientSecretDto>(operatorOnly);
		}

		var secret = ProductClientRules.NewSecret();

		return await store.RotateSecretAsync(tenantId, clientId, secret, cancellationToken).ConfigureAwait(false)
			? Result.Success(new ProductClientSecretDto(clientId, secret))
			: Result.Failure<ProductClientSecretDto>(SecureGateErrors.Clients.NotFound);
	}
}

/// <summary>Revoga o client removendo-o; repetir responde 404 sem outro efeito (ADR-0034).</summary>
public sealed class DeleteProductClientHandler(IProductClientStore store)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="clientId">Client da rota.</param>
	/// <param name="byTenantAdmin">Pela autoridade de admin de tenant: só client de máquina (ADR-0048).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(
		Guid tenantId, string clientId, bool byTenantAdmin = false, CancellationToken cancellationToken = default)
	{
		if (byTenantAdmin
			&& await ProductClientTenantAdminGuard.OperatorOnlyAsync(store, tenantId, clientId, cancellationToken).ConfigureAwait(false)
				is { } operatorOnly)
		{
			return Result.Failure(operatorOnly);
		}

		return await store.DeleteAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false)
			? Result.Success()
			: Result.Failure(SecureGateErrors.Clients.NotFound);
	}
}

/// <summary>
/// Client de login fica com o operador (ADR-0048), e client com gestão do tenant também (ADR-0052): a autoridade
/// de admin de tenant só mexe em máquina comum.
/// </summary>
internal static class ProductClientTenantAdminGuard
{
	/// <summary>Indica se a lista de escopos concede a gestão do tenant.</summary>
	public static bool GrantsTenantManage(IReadOnlyList<string>? scopes) =>
		scopes?.Contains(SecureGateScopes.TenantManage, StringComparer.Ordinal) == true;

	/// <summary>
	/// Erro quando só o operador pode mexer no client: interativo, ou com gestão do tenant — rotacionar entregaria o
	/// secret dele a quem não poderia tê-lo criado. Nulo quando o client não existe ou é máquina comum.
	/// </summary>
	public static async Task<Error?> OperatorOnlyAsync(
		IProductClientStore store, Guid tenantId, string clientId, CancellationToken cancellationToken) =>
		await store.GetAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false) switch
		{
			{ Type: ProductClientRules.AuthorizationCodeType } => SecureGateErrors.Clients.InteractiveRequiresOperator,
			{ } client when GrantsTenantManage(client.Scopes) => SecureGateErrors.Clients.TenantManageRequiresOperator,
			_ => null,
		};
}
