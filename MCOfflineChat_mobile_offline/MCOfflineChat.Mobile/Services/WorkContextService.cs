namespace MCOfflineChat.Mobile.Services;

public enum WorkOpenTarget
{
    Chat,
    Documents,
    Stories
}

public sealed class WorkContextService
{
    public WorkOpenTarget? PendingTarget { get; private set; }
    public string? PendingId { get; private set; }

    public void RequestOpen(WorkOpenTarget target, string id)
    {
        PendingTarget = target;
        PendingId = id;
    }

    public (WorkOpenTarget Target, string Id)? Consume()
    {
        if (PendingTarget == null || string.IsNullOrWhiteSpace(PendingId))
            return null;

        var value = (PendingTarget.Value, PendingId);
        PendingTarget = null;
        PendingId = null;
        return value;
    }
}
