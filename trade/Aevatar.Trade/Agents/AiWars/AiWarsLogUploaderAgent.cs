using Aevatar.Agents.Core;
using Aevatar.Trade.Infrastructure.AiWars;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.AiWars;

/// <summary>
/// AI Wars log uploader agent.
///
/// Data flow:
/// - Receives <see cref="AiWarsLogUploadRequestedEvent"/> (typically from TradeAuditAgent)
/// - Uploads artifact via <see cref="IWeexAiWarsLogClient"/>
/// - Publishes success/failure events upward for auditing
/// </summary>
public sealed class AiWarsLogUploaderAgent : GAgentBase<AiWarsUploaderState>
{
    public IWeexAiWarsLogClient? Client { get; set; }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        State.AgentId = Id.ToString();
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"AiWarsUploader: ok={State.UploadsSucceeded}, fail={State.UploadsFailed}, last={State.LastUploadTime?.ToDateTime():O}");
    }

    [Aevatar.Agents.Abstractions.Attributes.EventHandler]
    public async Task HandleUploadRequestedAsync(AiWarsLogUploadRequestedEvent evt)
    {
        State.UploadsAttempted++;

        if (Client == null)
        {
            await PublishFailedAsync(evt, "NO_CLIENT", "IWeexAiWarsLogClient is not configured.");
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(evt.ArtifactPath) || !File.Exists(evt.ArtifactPath))
            {
                await PublishFailedAsync(evt, "FILE_NOT_FOUND", $"Artifact not found: {evt.ArtifactPath}");
                return;
            }

            var bytes = await File.ReadAllBytesAsync(evt.ArtifactPath);
            var fileName = Path.GetFileName(evt.ArtifactPath);
            var contentType = string.IsNullOrWhiteSpace(evt.ContentType) ? "application/octet-stream" : evt.ContentType;

            var result = await Client.UploadAiLogAsync(new AiWarsUploadRequest
            {
                FileName = fileName,
                ContentType = contentType,
                Content = bytes,
                CycleId = string.IsNullOrWhiteSpace(evt.CycleId) ? null : evt.CycleId
            });

            State.LastUploadTime = Timestamp.FromDateTime(DateTime.UtcNow);

            if (result.Success)
            {
                State.UploadsSucceeded++;
                Logger.LogInformation("[AiWarsUploader] Upload succeeded: {File}", fileName);

                await PublishAsync(new AiWarsLogUploadSucceededEvent
                {
                    RequestId = evt.RequestId,
                    CycleId = evt.CycleId,
                    RemoteId = result.RemoteId ?? string.Empty,
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                }, Aevatar.Agents.EventDirection.Up);
            }
            else
            {
                State.UploadsFailed++;
                State.LastError = $"{result.ErrorCode}: {result.ErrorMessage}";

                await PublishFailedAsync(evt, result.ErrorCode ?? "UPLOAD_FAILED", result.ErrorMessage ?? "Upload failed.");
            }
        }
        catch (Exception ex)
        {
            State.UploadsFailed++;
            State.LastError = ex.Message;
            Logger.LogError(ex, "[AiWarsUploader] Upload exception");

            await PublishFailedAsync(evt, "CLIENT_ERROR", ex.Message);
        }
    }

    private async Task PublishFailedAsync(AiWarsLogUploadRequestedEvent req, string code, string message)
    {
        await PublishAsync(new AiWarsLogUploadFailedEvent
        {
            RequestId = req.RequestId,
            CycleId = req.CycleId,
            ErrorCode = code,
            ErrorMessage = message,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        }, Aevatar.Agents.EventDirection.Up);
    }
}


