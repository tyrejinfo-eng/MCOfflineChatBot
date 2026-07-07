namespace MCOfflineChat.Core.Interfaces;

/// <summary>
/// Capabilities an engine can advertise for routing events.
/// </summary>
[Flags]
public enum EngineCapability
{
    None = 0,
    TelemetryIngestion = 1 << 0,
    BehavioralDetection = 1 << 1,
    GraphAnalysis = 1 << 2,
    StaticCodeAnalysis = 1 << 3,
    MalwareAnalysis = 1 << 4,
    MLScoring = 1 << 5,
    AttackPrediction = 1 << 6,
    AutomatedResponse = 1 << 7,
    ThreatIntelEnrichment = 1 << 8,
}
