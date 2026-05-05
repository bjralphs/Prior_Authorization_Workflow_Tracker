namespace Prior_Authorization_Workflow_Tracker.Models;

/// <summary>Notification event types written by NotificationService.</summary>
public enum NotificationType
{
    ExpirationAlert,
    StatusChanged,
    AppealWindow,
    DecisionRendered
}
