using Aevatar.Agents.Abstractions.Attributes;

namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Test agent for metadata handling
/// </summary>
public class MetadataTestAgent : GAgentBase<TestAgentState>
{
    public Dictionary<string, string> ReceivedMetadata { get; private set; } = new();
    public bool ModifyMetadata { get; set; }

    public override string GetDescription() => "MetadataTestAgent";

    [AllEventHandler]
    public async Task HandleWithMetadata(EventEnvelope envelope)
    {
        // Capture received metadata from the event's payload (TestMetadata)
        if (envelope.Payload.Is(TestMetadata.Descriptor))
        {
            var testMetadata = envelope.Payload.Unpack<TestMetadata>();
            ReceivedMetadata = new Dictionary<string, string>(testMetadata.Value);

            // Optionally modify metadata
            if (ModifyMetadata)
            {
                ReceivedMetadata["modified"] = "true";
            }
        }

        await Task.CompletedTask;
    }
}