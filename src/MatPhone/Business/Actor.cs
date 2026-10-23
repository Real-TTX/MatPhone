using MatPhone.Data.Entities;
using MatPhone.Services;

namespace MatPhone.Business;

/// <summary>
/// Who calls the business layer. Every business function takes one and checks it itself (the pages check too, but a page is not the last line):
/// <see cref="RequireAdmin"/> for the administration, <see cref="RequireSelfOrAdmin"/> for what belongs to a person.
/// </summary>
public sealed record Actor(long? UserId, string? Name, bool IsAdmin)
{
    /// <summary>The telephony engine and the scheduled jobs: may do everything, is nobody.</summary>
    public static readonly Actor System = new(null, "System", true);

    public static Actor From(ICurrentUser user) => new(user.UserId, user.Name, user.IsAdmin);

    public bool IsSignedIn => UserId is not null;

    public void RequireAdmin()
    {
        if (!IsAdmin)
        {
            throw new AccessDeniedException();
        }
    }

    public void RequireSignedIn()
    {
        if (UserId is null && !IsAdmin)
        {
            throw new AccessDeniedException();
        }
    }

    /// <summary>The person themselves or an administrator.</summary>
    public void RequireSelfOrAdmin(long userId)
    {
        if (!IsAdmin && UserId != userId)
        {
            throw new AccessDeniedException();
        }
    }

    public long Me => UserId ?? throw new AccessDeniedException();

    public UserRole Role => IsAdmin ? UserRole.Admin : UserRole.User;
}

/// <summary>Thrown by the business layer when the actor may not do what was asked; a page answers 403 (<see cref="AccessDeniedFilter"/>).</summary>
public sealed class AccessDeniedException() : Exception("Access denied.");

/// <summary>
/// The outcome of a save: the id of the record, or the problems per field (the name of the input field, "Number", and a key of the texts). The page shows them
/// at the fields; a problem without a field ("") goes to the summary.
/// </summary>
public sealed class SaveResult
{
    public long Id { get; init; }

    public Dictionary<string, string> Errors { get; } = [];

    public bool Ok => Errors.Count == 0;

    public static SaveResult Saved(long id) => new() { Id = id };

    public static SaveResult Fail(string field, string message)
    {
        var result = new SaveResult();
        result.Errors[field] = message;
        return result;
    }

    public SaveResult Add(string field, string message)
    {
        Errors.TryAdd(field, message);
        return this;
    }
}
