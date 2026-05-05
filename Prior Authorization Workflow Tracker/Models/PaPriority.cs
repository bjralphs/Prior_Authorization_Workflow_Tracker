namespace Prior_Authorization_Workflow_Tracker.Models;

/// <summary>Priority level of a PA request; drives SLA deadline calculation (BR-002).</summary>
public enum PaPriority
{
    Routine,
    Urgent,
    Emergent
}
