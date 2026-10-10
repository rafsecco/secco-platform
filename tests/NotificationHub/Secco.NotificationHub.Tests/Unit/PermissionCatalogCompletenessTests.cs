using System.Reflection;
using FluentAssertions;
using Secco.NotificationHub.Application;
using Xunit;

namespace Secco.NotificationHub.Tests.Unit;

/// <summary>
/// O catálogo publicado (ADR-0039) precisa conter toda constante de <see cref="NotificationHubPermissions"/>:
/// permissão nova sem entrada no <c>Catalog</c> seria recusada pelo SecureGate ao atribuir a um perfil.
/// </summary>
public class PermissionCatalogCompletenessTests
{
	[Fact]
	public void Catalog_ContemTodaConstanteDePermissao()
	{
		var constants = typeof(NotificationHubPermissions).GetNestedTypes()
			.SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
			.Where(field => field.IsLiteral && field.FieldType == typeof(string))
			.Select(field => (string)field.GetRawConstantValue()!)
			.ToHashSet(StringComparer.Ordinal);

		NotificationHubPermissions.Catalog.Select(p => p.Name).Should().BeEquivalentTo(constants);
		NotificationHubPermissions.Catalog.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Description));
	}
}
