using Prior_Authorization_Workflow_Tracker.Models;

namespace Prior_Authorization_Workflow_Tracker.Exceptions;

/// <summary>
/// Thrown by WorkflowService when a requested status transition is not permitted
/// by the state machine definition (FR-004, §8.4).
/// </summary>
public class WorkflowTransitionException : PaException
{
    public PaStatus FromStatus { get; }
    public PaStatus ToStatus { get; }

    public WorkflowTransitionException(PaStatus from, PaStatus to)
        : base($"Transition from {from} to {to} is not permitted.")
    {
        FromStatus = from;
        ToStatus = to;
    }
}
