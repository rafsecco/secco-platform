using Secco.SecureGate.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application;

/// <summary>Erros de negócio do SecureGate (ADR-0004): códigos estáveis <c>SecureGate.*</c>.</summary>
public static class SecureGateErrors
{
    /// <summary>Erros do catálogo de tenants (gestão).</summary>
    public static class Tenants
    {
        /// <summary>Tenant não encontrado no catálogo.</summary>
        public static readonly Error NotFound =
            Error.NotFound("SecureGate.Tenant.NotFound", "Tenant não encontrado.");

        /// <summary>Nome do tenant ausente.</summary>
        public static readonly Error NameRequired =
            Error.Validation("SecureGate.Tenant.NameRequired", "O nome do tenant é obrigatório.");

        /// <summary>Nome do tenant acima do limite.</summary>
        public static readonly Error NameTooLong =
            Error.Validation("SecureGate.Tenant.NameTooLong",
                $"O nome do tenant excede o limite de {Tenant.NameMaxLength} caracteres.");

        /// <summary>Slug ausente ou fora do formato aceito.</summary>
        public static readonly Error SlugInvalid =
            Error.Validation("SecureGate.Tenant.SlugInvalid",
                $"O slug deve ser kebab-case minúsculo (letras, dígitos e hífens) com até {Tenant.SlugMaxLength} caracteres.");

        /// <summary>Já existe tenant com o slug informado.</summary>
        public static readonly Error SlugAlreadyExists =
            Error.Conflict("SecureGate.Tenant.SlugAlreadyExists", "Já existe um tenant com este slug.");
    }

    /// <summary>Erros de bancos de tenant (gestão).</summary>
    public static class TenantDatabases
    {
        /// <summary>Produto ausente ou fora do formato aceito.</summary>
        public static readonly Error ProductInvalid =
            Error.Validation("SecureGate.TenantDatabase.ProductInvalid",
                $"O produto deve ser kebab-case minúsculo (letras, dígitos e hífens) com até {TenantDatabase.ProductMaxLength} caracteres.");

        /// <summary>Connection string ausente.</summary>
        public static readonly Error ConnectionStringRequired =
            Error.Validation("SecureGate.TenantDatabase.ConnectionStringRequired",
                "A connection string é obrigatória.");

        /// <summary>Connection string acima do limite (o valor nunca entra na mensagem — ADR-0020).</summary>
        public static readonly Error ConnectionStringTooLong =
            Error.Validation("SecureGate.TenantDatabase.ConnectionStringTooLong",
                $"A connection string excede o limite de {TenantDatabase.ConnectionStringMaxLength} caracteres.");
    }

    /// <summary>Erros de roles e permissões (ADR-0021).</summary>
    public static class Roles
    {
        /// <summary>Role não encontrado no tenant.</summary>
        public static readonly Error NotFound =
            Error.NotFound("SecureGate.Role.NotFound", "Role não encontrado neste tenant.");

        /// <summary>Nome de role ausente ou fora do formato aceito.</summary>
        public static readonly Error NameInvalid =
            Error.Validation("SecureGate.Role.NameInvalid",
                "O nome do role deve conter apenas letras, dígitos, '.', '_' e '-' (sem espaços), com até 100 caracteres.");

        /// <summary>Já existe role com este nome no tenant.</summary>
        public static readonly Error AlreadyExists =
            Error.Conflict("SecureGate.Role.AlreadyExists", "Já existe um role com este nome neste tenant.");

        /// <summary>Permissão fora do formato canônico.</summary>
        public static readonly Error PermissionInvalid =
            Error.Validation("SecureGate.Role.PermissionInvalid",
                "Cada permissão deve estar no formato canônico 'recurso:acao' (kebab-case minúsculo).");

        /// <summary>Conjunto de permissões acima do limite.</summary>
        public static readonly Error TooManyPermissions =
            Error.Validation("SecureGate.Role.TooManyPermissions",
                "O conjunto de permissões excede o limite por role.");
    }

    /// <summary>Erros do endpoint de catálogo (leitura pelos produtos).</summary>
    public static class Catalog
    {
        /// <summary>Produto da rota fora do formato aceito.</summary>
        public static readonly Error ProductInvalid =
            Error.Validation("SecureGate.Catalog.ProductInvalid",
                "O produto informado na rota é inválido.");

        /// <summary>
        /// Entrada de catálogo inexistente — mensagem única para tenant desconhecido,
        /// desativado ou sem banco no produto (não revelar qual caso ocorreu, ADR-0020).
        /// </summary>
        public static readonly Error EntryNotFound =
            Error.NotFound("SecureGate.Catalog.EntryNotFound",
                "Não há banco cadastrado para este tenant neste produto.");
    }

    /// <summary>Erros de provisionamento de usuários (Fase 6.5).</summary>
    public static class Users
    {
        /// <summary>E-mail ausente ou fora do formato aceito.</summary>
        public static readonly Error EmailInvalid =
            Error.Validation("SecureGate.User.EmailInvalid", "Informe um e-mail válido.");

        /// <summary>Senha ausente.</summary>
        public static readonly Error PasswordRequired =
            Error.Validation("SecureGate.User.PasswordRequired", "A senha é obrigatória.");

        /// <summary>Algum role informado não existe no tenant.</summary>
        public static readonly Error RoleNotFound =
            Error.Validation("SecureGate.User.RoleNotFound", "Um dos roles informados não existe neste tenant.");

        /// <summary>Já existe usuário com este e-mail.</summary>
        public static readonly Error AlreadyExists =
            Error.Conflict("SecureGate.User.AlreadyExists", "Já existe um usuário com este e-mail.");

        /// <summary>Falha de validação do Identity (senha fraca etc.); o detalhe vem do Identity.</summary>
        /// <param name="detail">Descrição agregada das regras violadas (nunca revela se o e-mail existe).</param>
        public static Error CreationFailed(string detail) =>
            Error.Validation("SecureGate.User.CreationFailed", detail);
    }
}
