namespace Prior_Authorization_Workflow_Tracker.Exceptions;

/// <summary>
/// Thrown when a service method cannot locate the requested entity (§17.1).
/// Results in a 404-style user-facing error, not a generic 500.
/// </summary>
public class EntityNotFoundException : PaException
{
    public string EntityName { get; }
    public string EntityId { get; }

    public EntityNotFoundException(string entityName, string entityId)
        : base($"{entityName} with ID '{entityId}' was not found.")
    {
        EntityName = entityName;
        EntityId = entityId;
    }
}
