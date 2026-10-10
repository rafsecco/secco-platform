using System.Reflection;
using FluentAssertions;
using Secco.LogStream.Application;
using Xunit;

namespace Secco.LogStream.Tests.Unit;

/// <summary>
/// O catálogo publicado (ADR-0039) precisa conter toda constante de <see cref="LogStreamPermissions"/>:
/// permissão nova sem entrada no <c>Catalog</c> seria recusada pelo SecureGate ao atribuir a um perfil.
/// </summary>
public class PermissionCatalogCompletenessTests
{
	[Fact]
	public void Catalog_ContemTodaConstanteDePermissao()
	{
		var constants = typeof(LogStreamPermissions).GetNestedTypes()
			.SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
			.Where(field => field.IsLiteral && field.FieldType == typeof(string))
			.Select(field => (string)field.GetRawConstantValue()!)
			.ToHashSet(StringComparer.Ordinal);

		LogStreamPermissions.Catalog.Select(p => p.Name).Should().BeEquivalentTo(constants);
		LogStreamPermissions.Catalog.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Description));
	}
}
