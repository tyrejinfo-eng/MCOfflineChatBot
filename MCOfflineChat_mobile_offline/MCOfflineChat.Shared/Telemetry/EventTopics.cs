namespace MCOfflineChat.Shared.Telemetry;

/// <summary>
/// v1.1.72: Centralized EventBus topic constants. Eliminates magic strings across 78+ engines.
/// Usage: EventBus.Publish(new TelemetryEvent { EventType = EventTopics.Scan.Completed, ... });
/// </summary>
public static class EventTopics
{
    public static class Scan
    {
        public const string Started = "scan.started";
        public const string Completed = "scan.completed";
        public const string ThreatFound = "scan.threat.found";
        public const string FileScanned = "scan.file.scanned";
        public const string QuarantineAction = "scan.quarantine";
    }

    public static class Detection
    {
        public const string Raised = "detection.raised";
        public const string Dismissed = "detection.dismissed";
        public const string Escalated = "detection.escalated";
    }

    public static class Alert
    {
        public const string Created = "alert.created";
        public const string Acknowledged = "alert.acknowledged";
        public const string Resolved = "alert.resolved";
    }

    public static class Engine
    {
        public const string Started = "engine.started";
        public const string Stopped = "engine.stopped";
        public const string Error = "engine.error";
        public const string HealthCheck = "engine.health";
        public const string WatchdogRestart = "engine.watchdog.restart";
    }

    public static class Network
    {
        public const string ConnectionBlocked = "network.blocked";
        public const string TrafficAnomaly = "network.anomaly";
        public const string DnsQuery = "network.dns";
        public const string VpnServersUpdated = "vpn.servers.updated";
    }

    public static class Llm
    {
        public const string ModelMounted = "llm.model.mounted";
        public const string ModelUnmounted = "llm.model.unmounted";
        public const string InferenceComplete = "llm.inference.complete";
        public const string ModelError = "llm.model.error";
    }

    public static class Auth
    {
        public const string LoginSuccess = "auth.login.success";
        public const string LoginFailed = "auth.login.failed";
        public const string TokenRefreshed = "auth.token.refreshed";
        public const string AccountLocked = "auth.account.locked";
    }

    public static class Telemetry
    {
        public const string Heartbeat = "telemetry.heartbeat";
        public const string MetricsSnapshot = "telemetry.metrics";
        public const string ClientConnected = "client.connected";
        public const string ClientDisconnected = "client.disconnected";
    }

    public static class Intelligence
    {
        public const string IocDiscovered = "intel.ioc.discovered";
        public const string FeedUpdated = "intel.feed.updated";
        public const string StixExported = "intel.stix.exported";
        public const string SigmaGenerated = "intel.sigma.generated";
    }

    public static class Soc
    {
        public const string IncidentCreated = "soc.incident.created";
        public const string IncidentResolved = "soc.incident.resolved";
        public const string QueryProcessed = "soc.query.processed";
    }

    public static class AttackGraph
    {
        public const string ChainCreated = "attack.chain.created";
        public const string ChainUpdated = "attack.chain.updated";
        public const string ChainClosed = "attack.chain.closed";
    }

    public static class Agent
    {
        public const string TaskSubmitted = "agent.task.submitted";
        public const string TaskCompleted = "agent.task.completed";
        public const string TaskFailed = "agent.task.failed";
    }

    public static class Federation
    {
        public const string PeerConnected = "federation.peer.connected";
        public const string PeerDisconnected = "federation.peer.disconnected";
        public const string IocShared = "federation.ioc.shared";
    }

    // v1.1.90: Crawler and DDoS topics (used by CrawlerIntelligenceEngine, DDoSDetectionEngine, WebsiteSecurityViewModel)
    public static class Crawler
    {
        public const string VisitorDetected = "crawler.visitor.detected";
        public const string IpBlocked = "crawler.ip.blocked";
        public const string Classified = "crawler.classified";
        public const string Blocked = "crawler.blocked";
        public const string WebRequestReceived = "web.request.received";
    }

    public static class DDoS
    {
        public const string Detected = "ddos.detected";
        public const string MitigationBlock = "ddos.mitigation.block";
    }

    public static class Process
    {
        public const string Created = "process.created";
        public const string Terminated = "process.terminated";
        public const string TreeUpdated = "process.tree.updated";
        public const string SuspiciousChain = "process.suspicious.chain";
        public const string GraphChainDetected = "process.graph.chain.detected";
    }

    public static class File
    {
        public const string Modified = "file.modified";
        public const string Created = "file.created";
        public const string Deleted = "file.deleted";
        public const string Suspicious = "file.suspicious";
    }

    public static class Registry
    {
        public const string Modified = "registry.modified";
        public const string PersistenceDetected = "registry.persistence.detected";
    }

    public static class Threat
    {
        public const string Scored = "threat.scored";
        public const string GraphUpdated = "threat.graph.updated";
    }

    public static class Swarm
    {
        public const string SignalReceived = "swarm.signal.received";
        public const string NodeConnected = "swarm.node.connected";
        public const string StorageSync = "swarm.storage.sync";
    }
}
