namespace DbaTool.Api.Common;

public sealed class EntityNotFoundException : Exception
{
    public string EntityName { get; }
    public object Key { get; }

    public EntityNotFoundException(string entityName, object key)
        : base($"{entityName} #{key} not found.")
    {
        EntityName = entityName;
        Key = key;
    }

    public EntityNotFoundException(string message)
        : base(message)
    {
        EntityName = "Resource";
        Key = string.Empty;
    }
}

public sealed class DomainValidationException(string message)
    : Exception(message);

public sealed class ResourceInUseException(string message)
    : Exception(message);
