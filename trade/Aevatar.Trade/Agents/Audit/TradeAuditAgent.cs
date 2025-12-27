using System.Text;
using System.Text.Json;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.Audit;

/// <summary>
/// Trade audit agent (JSONL sink).
///
/// Goals:
/// - Capture key events for replay/debug/demo.
/// - Produce artifacts that can later be uploaded to WEEX AI Wars "Upload AI log".
///
/// NOTE:
/// - This agent intentionally writes *append-only* JSONL for simplicity and robustness.
/// - High-frequency market data logging is disabled by default to avoid huge files.
/// </summary>
public sealed class TradeAuditAgent : GAgentBase<TradeAuditState>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly TypeRegistry TradeTypeRegistry = TypeRegistry.FromMessages(
        // Core trade events
        MarketTickEvent.Descriptor,
        KlineUpdateEvent.Descriptor,
        MarketSentimentAnalysisEvent.Descriptor,
        TechnicalAnalysisEvent.Descriptor,
        NewsImpactAnalysisEvent.Descriptor,
        TradingDecisionEvent.Descriptor,
        DecisionCycleStartedEvent.Descriptor,
        DecisionCycleCompletedEvent.Descriptor,
        ApprovedTradeEvent.Descriptor,
        TradeRejectedEvent.Descriptor,
        OrderExecutedEvent.Descriptor,
        OrderSimulatedEvent.Descriptor,
        OrderFailedEvent.Descriptor,
        OrderCancelledEvent.Descriptor,
        CircuitBreakerTriggeredEvent.Descriptor,
        AiWarsLogUploadRequestedEvent.Descriptor,
        AiWarsLogUploadSucceededEvent.Descriptor,
        AiWarsLogUploadFailedEvent.Descriptor
    );

    private static readonly JsonFormatter EnvelopeJsonFormatter =
        new(new JsonFormatter.Settings(formatDefaultValues: true, typeRegistry: TradeTypeRegistry));

    private bool _includeMarketData;
    private bool _requestAiWarsUpload;

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // Stable unified id (AgentType:RawId)
        State.AgentId = Id.ToString();

        if (string.IsNullOrWhiteSpace(State.AuditRunId))
        {
            State.AuditRunId = Guid.NewGuid().ToString("N")[..12];
        }

        if (string.IsNullOrWhiteSpace(State.OutputDir))
        {
            // Default to a local folder under current working directory
            State.OutputDir = "trade-audit";
        }

        if (string.IsNullOrWhiteSpace(State.CurrentFile))
        {
            State.CurrentFile = $"trade_audit_{State.AuditRunId}.jsonl";
        }

        Directory.CreateDirectory(State.OutputDir);

        Logger.LogInformation(
            "[TradeAudit] Activated: AgentId={AgentId}, Run={RunId}, Output={OutputDir}/{File}",
            State.AgentId, State.AuditRunId, State.OutputDir, State.CurrentFile);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"TradeAudit: Run={State.AuditRunId}, Logged={State.EventsLogged}, File={State.OutputDir}/{State.CurrentFile}");
    }

    /// <summary>
    /// Configure audit behavior (called by TradingSystem during initialization).
    /// </summary>
    public void Configure(TradeAuditConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        _includeMarketData = config.IncludeMarketData;
        _requestAiWarsUpload = config.RequestAiwarsUpload;

        if (!string.IsNullOrWhiteSpace(config.OutputDir))
        {
            State.OutputDir = config.OutputDir.Trim();
        }

        // Rotate file for new config/session if needed (simple strategy)
        if (string.IsNullOrWhiteSpace(State.AuditRunId))
            State.AuditRunId = Guid.NewGuid().ToString("N")[..12];

        State.CurrentFile = $"trade_audit_{State.AuditRunId}.jsonl";
        Directory.CreateDirectory(State.OutputDir);

        Logger.LogInformation(
            "[TradeAudit] Configured: IncludeMarketData={Market}, RequestAiWarsUpload={Upload}, Output={OutputDir}/{File}",
            _includeMarketData, _requestAiWarsUpload, State.OutputDir, State.CurrentFile);
    }

    [AllEventHandler(AllowSelfHandling = true)]
    public async Task HandleAnyEventAsync(EventEnvelope envelope)
    {
        // Filter: by default skip very noisy market data
        if (!_includeMarketData && IsMarketData(envelope))
            return;

        var line = BuildJsonLine(envelope);
        await AppendLineAsync(line);

        State.EventsLogged++;
        State.LastEventTime = Timestamp.FromDateTime(DateTime.UtcNow);

        // Optional: emit upload request event when a decision cycle completes
        if (_requestAiWarsUpload && TryExtractDecisionCycleCompleted(envelope, out var completed))
        {
            var artifactPath = Path.Combine(State.OutputDir, State.CurrentFile);
            await PublishAsync(new AiWarsLogUploadRequestedEvent
            {
                RequestId = Guid.NewGuid().ToString("N")[..16],
                CycleId = completed.CycleId,
                ArtifactPath = artifactPath,
                ContentType = "application/jsonl",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
    }

    private static bool IsMarketData(EventEnvelope envelope)
    {
        var typeUrl = envelope.Payload?.TypeUrl ?? string.Empty;
        return typeUrl.EndsWith(nameof(MarketTickEvent), StringComparison.Ordinal) ||
               typeUrl.EndsWith(nameof(KlineUpdateEvent), StringComparison.Ordinal);
    }

    private static bool TryExtractDecisionCycleCompleted(EventEnvelope envelope, out DecisionCycleCompletedEvent evt)
    {
        evt = new DecisionCycleCompletedEvent();
        if (envelope.Payload == null) return false;

        // Any.Unpack<T> works when type matches; otherwise throws.
        try
        {
            evt = envelope.Payload.Unpack<DecisionCycleCompletedEvent>();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string BuildJsonLine(EventEnvelope envelope)
    {
        // Convert EventEnvelope (with Any payload) to JSON. Registry provides best-effort Any unpack.
        var envelopeJson = EnvelopeJsonFormatter.Format(envelope);

        // Wrap with audit metadata to make ingestion easier
        var wrapper = new
        {
            auditRunId = State.AuditRunId,
            auditAgentId = State.AgentId,
            envelopeJson
        };

        return JsonSerializer.Serialize(wrapper, JsonOptions);
    }

    private async Task AppendLineAsync(string line)
    {
        var path = Path.Combine(State.OutputDir, State.CurrentFile);

        // Append-only file IO (robust, no long-lived handles)
        await File.AppendAllTextAsync(path, line + "\n", Encoding.UTF8);
    }
}


