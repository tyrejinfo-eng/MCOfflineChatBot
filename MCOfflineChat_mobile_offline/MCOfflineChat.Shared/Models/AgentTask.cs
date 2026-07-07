namespace MCOfflineChat.Shared.Models;

public sealed class AgentTask
{
    public string TaskId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Prompt { get; set; } = string.Empty;
    public AgentTaskStatus Status { get; set; } = AgentTaskStatus.Pending;
    public string? Plan { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string CreatedBy { get; set; } = "admin";
}

public enum AgentTaskStatus
{
    Pending,
    Planning,
    Validating,
    Executing,
    Complete,
    Failed,
    Cancelled
}
