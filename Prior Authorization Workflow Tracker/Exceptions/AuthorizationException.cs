namespace Prior_Authorization_Workflow_Tracker.Exceptions;

/// <summary>
/// Thrown when the current user attempts an operation they are not authorized to perform (§10, §17.1).
/// Caught by middleware and logged at Warning level per NFR-006.
/// </summary>
public class AuthorizationException : PaException
{
    public AuthorizationException(string message) : base(message) { }
}
