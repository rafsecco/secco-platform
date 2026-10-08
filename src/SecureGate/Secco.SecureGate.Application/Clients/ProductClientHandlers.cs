using Secco.SecureGate.Application.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Clients;

/// <summary>Registra client de produto vinculado ao tenant (ADR-0037). Secret exibido uma única vez.</summary>
public sealed class CreateProductClientHandler(
	ITenantRepository tenants, ProductClientAccessValidator validator, IProductClientStore store)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<CreatedProductClientDto>> HandleAsync(
		Guid tenantId, ProductClientCommand command, CancellationToken cancellationToken = default)
	{
		if (await tenants.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false) is not { } tenant)
		{
			return Result.Failure<CreatedProductClientDto>(SecureGateErrors.Tenants.NotFound);
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
			created.ClientId, created.Name, created.Scopes, created.Roles, created.CreatedAt, secret));
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
	public async Task<Result> HandleAsync(
		Guid tenantId, string clientId, ProductClientCommand command, CancellationToken cancellationToken = default)
	{
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
	public async Task<Result<ProductClientSecretDto>> HandleAsync(
		Guid tenantId, string clientId, CancellationToken cancellationToken = default)
	{
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
	public async Task<Result> HandleAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default) =>
		await store.DeleteAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false)
			? Result.Success()
			: Result.Failure(SecureGateErrors.Clients.NotFound);
}
