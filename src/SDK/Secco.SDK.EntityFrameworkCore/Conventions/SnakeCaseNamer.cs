using System.Text;

namespace Secco.SDK.EntityFrameworkCore.Conventions;

/// <summary>Tradução de identificadores C# para os nomes minúsculos da ADR-0017.</summary>
internal static class SnakeCaseNamer
{
    /// <summary>Converte PascalCase/camelCase para snake_case (LogEntry → log_entry, IPAddress → ip_address).</summary>
    public static string ToSnakeCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 8);

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];

            if (char.IsUpper(current))
            {
                var previousIsLowerOrDigit = i > 0 && (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1]));
                var nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);

                if (i > 0 && (previousIsLowerOrDigit || nextIsLower))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }

    /// <summary>Remove o prefixo booleano do C# (IsActive → Active, HasErrors → Errors) — ADR-0017.</summary>
    public static string StripBooleanPrefix(string name)
    {
        if (name.Length > 2 && name.StartsWith("Is", StringComparison.Ordinal) && char.IsUpper(name[2]))
        {
            return name[2..];
        }

        if (name.Length > 3 && name.StartsWith("Has", StringComparison.Ordinal) && char.IsUpper(name[3]))
        {
            return name[3..];
        }

        return name;
    }

    /// <summary>Remove o sufixo "Id" de propriedades de chave (TenantId → Tenant).</summary>
    public static string StripIdSuffix(string name) =>
        name.Length > 2 && name.EndsWith("Id", StringComparison.Ordinal) ? name[..^2] : name;
}
