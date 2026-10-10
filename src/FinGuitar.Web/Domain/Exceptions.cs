namespace FinGuitar.Web.Domain;

/// <summary>A business rule was broken, for example an order status change that is not allowed.</summary>
public class DomainException(string message) : Exception(message);

/// <summary>The thing asked for does not exist, for example "Order 999 not found."</summary>
public class NotFoundException(string message) : DomainException(message);

/// <summary>Input failed validation. Carries one message per field.</summary>
public class ValidationException(IReadOnlyDictionary<string, string> errors)
    : Exception(string.Join(" ", errors.Values))
{
    public IReadOnlyDictionary<string, string> Errors { get; } = errors;
}

/// <summary>The signed-in user is not allowed to do this.</summary>
public class ForbiddenException(string message = "You do not have permission to do this.") : Exception(message);
