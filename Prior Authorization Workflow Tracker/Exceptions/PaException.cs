namespace Prior_Authorization_Workflow_Tracker.Exceptions;

/// <summary>
/// Base exception for all application-specific errors (§17.1).
/// Caught at the middleware level; error-level entry written to Serilog.
/// </summary>
public class PaException : Exception
{
    public PaException(string message) : base(message) { }
    public PaException(string message, Exception inner) : base(message, inner) { }
}
