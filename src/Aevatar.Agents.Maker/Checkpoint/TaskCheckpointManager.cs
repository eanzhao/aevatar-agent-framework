using System.Collections.Concurrent;
using System.Text.Json;
using Aevatar.Agents.Maker.Messages;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker.Checkpoint;

// ============================================================
//  Task Checkpoint Manager
//  Implements checkpointing for task execution state recovery.
//
//  Design Philosophy:
//  - Every completed subtask is a checkpoint
//  - Crash recovery should resume from last checkpoint
//  - State must be serializable (Protobuf-compatible)
//  - Minimal overhead for normal execution
// ============================================================

/// <summary>
/// Checkpoint for a task execution state.
/// </summary>
public sealed record TaskCheckpoint
{
    /// <summary>Unique execution ID.</summary>
    public required string ExecutionId { get; init; }

    /// <summary>Task ID being checkpointed.</summary>
    public required string TaskId { get; init; }

    /// <summary>Task description.</summary>
    public required string Description { get; init; }

    /// <summary>Current execution phase.</summary>
    public required string Phase { get; init; }

    /// <summary>Depth in task tree.</summary>
    public int Depth { get; init; }

    /// <summary>Result content (if completed).</summary>
    public string? Result { get; init; }

    /// <summary>Completed subtask results.</summary>
    public Dictionary<string, string> SubtaskResults { get; init; } = new();

    /// <summary>Pending subtask IDs.</summary>
    public List<string> PendingSubtasks { get; init; } = [];

    /// <summary>Execution context.</summary>
    public Dictionary<string, string> Context { get; init; } = new();

    /// <summary>Timestamp when checkpoint was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>LLM calls consumed up to this point.</summary>
    public int LlmCallsConsumed { get; init; }

    /// <summary>Tokens consumed up to this point.</summary>
    public long TokensConsumed { get; init; }

    /// <summary>Red flags encountered.</summary>
    public List<string> RedFlags { get; init; } = [];
}

/// <summary>
/// Full execution checkpoint including task tree state.
/// </summary>
public sealed record ExecutionCheckpoint
{
    public required string ExecutionId { get; init; }
    public required string RootTaskDescription { get; init; }
    public required MakerOptions Options { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime CheckpointedAt { get; init; } = DateTime.UtcNow;

    /// <summary>All task checkpoints in this execution.</summary>
    public Dictionary<string, TaskCheckpoint> TaskCheckpoints { get; init; } = new();

    /// <summary>Root task checkpoint ID.</summary>
    public string? RootTaskId { get; init; }

    /// <summary>Total LLM calls so far.</summary>
    public int TotalLlmCalls { get; init; }

    /// <summary>Total tokens used so far.</summary>
    public long TotalTokens { get; init; }
}

/// <summary>
/// Recovery result from checkpoint.
/// </summary>
public sealed record RecoveryResult
{
    public bool CanResume { get; init; }
    public ExecutionCheckpoint? Checkpoint { get; init; }
    public string? ResumableTaskId { get; init; }
    public Dictionary<string, string>? CompletedResults { get; init; }
    public string? RecoveryMessage { get; init; }
}

/// <summary>
/// Persistence interface for checkpoints.
/// </summary>
public interface ICheckpointStore
{
    Task SaveCheckpointAsync(ExecutionCheckpoint checkpoint, CancellationToken ct = default);
    Task<ExecutionCheckpoint?> LoadCheckpointAsync(string executionId, CancellationToken ct = default);
    Task DeleteCheckpointAsync(string executionId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListCheckpointsAsync(CancellationToken ct = default);
}

/// <summary>
/// In-memory checkpoint store (for testing and Local runtime).
/// </summary>
public sealed class InMemoryCheckpointStore : ICheckpointStore
{
    private readonly ConcurrentDictionary<string, ExecutionCheckpoint> _checkpoints = new();

    public Task SaveCheckpointAsync(ExecutionCheckpoint checkpoint, CancellationToken ct = default)
    {
        _checkpoints[checkpoint.ExecutionId] = checkpoint;
        return Task.CompletedTask;
    }

    public Task<ExecutionCheckpoint?> LoadCheckpointAsync(string executionId, CancellationToken ct = default)
    {
        _checkpoints.TryGetValue(executionId, out var checkpoint);
        return Task.FromResult(checkpoint);
    }

    public Task DeleteCheckpointAsync(string executionId, CancellationToken ct = default)
    {
        _checkpoints.TryRemove(executionId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListCheckpointsAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(_checkpoints.Keys.ToList());
    }
}

/// <summary>
/// File-based checkpoint store for persistent recovery.
/// </summary>
public sealed class FileCheckpointStore : ICheckpointStore
{
    private readonly string _directory;
    private readonly ILogger? _logger;

    public FileCheckpointStore(string directory, ILogger? logger = null)
    {
        _directory = directory;
        _logger = logger;

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task SaveCheckpointAsync(ExecutionCheckpoint checkpoint, CancellationToken ct = default)
    {
        var path = GetPath(checkpoint.ExecutionId);
        var json = JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, ct);
        _logger?.LogDebug("[CHECKPOINT] Saved checkpoint for {ExecutionId} to {Path}", checkpoint.ExecutionId, path);
    }

    public async Task<ExecutionCheckpoint?> LoadCheckpointAsync(string executionId, CancellationToken ct = default)
    {
        var path = GetPath(executionId);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<ExecutionCheckpoint>(json);
    }

    public Task DeleteCheckpointAsync(string executionId, CancellationToken ct = default)
    {
        var path = GetPath(executionId);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger?.LogDebug("[CHECKPOINT] Deleted checkpoint for {ExecutionId}", executionId);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListCheckpointsAsync(CancellationToken ct = default)
    {
        var files = Directory.GetFiles(_directory, "*.checkpoint.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null)
            .Select(n => n!.Replace(".checkpoint", ""))
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    private string GetPath(string executionId) =>
        Path.Combine(_directory, $"{executionId}.checkpoint.json");
}

/// <summary>
/// Task Checkpoint Manager - manages checkpointing and recovery.
/// </summary>
public sealed class TaskCheckpointManager
{
    private readonly ICheckpointStore _store;
    private readonly ILogger? _logger;

    // In-flight checkpoint for current execution
    private ExecutionCheckpoint? _currentCheckpoint;
    private readonly object _checkpointLock = new();

    public TaskCheckpointManager(ICheckpointStore store, ILogger? logger = null)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Initialize checkpoint for a new execution.
    /// </summary>
    public void BeginExecution(string executionId, string rootTaskDescription, MakerOptions options)
    {
        lock (_checkpointLock)
        {
            _currentCheckpoint = new ExecutionCheckpoint
            {
                ExecutionId = executionId,
                RootTaskDescription = rootTaskDescription,
                Options = options,
                StartedAt = DateTime.UtcNow
            };

            _logger?.LogInformation("[CHECKPOINT] Started tracking execution {ExecutionId}", executionId);
        }
    }

    /// <summary>
    /// Record task start (creates pending checkpoint).
    /// </summary>
    public void OnTaskStarted(string taskId, string description, int depth, Dictionary<string, string> context)
    {
        lock (_checkpointLock)
        {
            if (_currentCheckpoint == null) return;

            var taskCheckpoint = new TaskCheckpoint
            {
                ExecutionId = _currentCheckpoint.ExecutionId,
                TaskId = taskId,
                Description = description,
                Phase = "Started",
                Depth = depth,
                Context = new Dictionary<string, string>(context)
            };

            _currentCheckpoint.TaskCheckpoints[taskId] = taskCheckpoint;

            _logger?.LogDebug("[CHECKPOINT] Task {TaskId} started at depth {Depth}", taskId, depth);
        }
    }

    /// <summary>
    /// Record task decomposition (checkpoints subtask structure).
    /// </summary>
    public void OnTaskDecomposed(string taskId, List<string> subtaskIds)
    {
        lock (_checkpointLock)
        {
            if (_currentCheckpoint?.TaskCheckpoints.TryGetValue(taskId, out var checkpoint) == true)
            {
                _currentCheckpoint.TaskCheckpoints[taskId] = checkpoint with
                {
                    Phase = "Decomposed",
                    PendingSubtasks = subtaskIds.ToList()
                };

                _logger?.LogDebug("[CHECKPOINT] Task {TaskId} decomposed into {Count} subtasks", taskId, subtaskIds.Count);
            }
        }
    }

    /// <summary>
    /// Record task completion (critical checkpoint).
    /// </summary>
    public async Task OnTaskCompletedAsync(
        string taskId,
        string? result,
        int llmCalls,
        long tokens,
        CancellationToken ct = default)
    {
        lock (_checkpointLock)
        {
            if (_currentCheckpoint?.TaskCheckpoints.TryGetValue(taskId, out var checkpoint) == true)
            {
                _currentCheckpoint.TaskCheckpoints[taskId] = checkpoint with
                {
                    Phase = "Completed",
                    Result = result,
                    LlmCallsConsumed = llmCalls,
                    TokensConsumed = tokens
                };

                // Update parent's subtask results if this is a child task
                UpdateParentSubtaskResult(taskId, result);

                _logger?.LogDebug("[CHECKPOINT] Task {TaskId} completed, result length: {Length}",
                    taskId, result?.Length ?? 0);
            }
        }

        // Persist checkpoint after significant task completion
        await PersistCheckpointAsync(ct);
    }

    /// <summary>
    /// Record red flag for task.
    /// </summary>
    public void OnRedFlag(string taskId, string reason)
    {
        lock (_checkpointLock)
        {
            if (_currentCheckpoint?.TaskCheckpoints.TryGetValue(taskId, out var checkpoint) == true)
            {
                var flags = checkpoint.RedFlags.ToList();
                flags.Add(reason);
                _currentCheckpoint.TaskCheckpoints[taskId] = checkpoint with { RedFlags = flags };
            }
        }
    }

    /// <summary>
    /// Complete execution and clean up checkpoint.
    /// </summary>
    public async Task CompleteExecutionAsync(CancellationToken ct = default)
    {
        string? executionId;
        lock (_checkpointLock)
        {
            executionId = _currentCheckpoint?.ExecutionId;
            _currentCheckpoint = null;
        }

        if (executionId != null)
        {
            await _store.DeleteCheckpointAsync(executionId, ct);
            _logger?.LogInformation("[CHECKPOINT] Execution {ExecutionId} completed, checkpoint removed", executionId);
        }
    }

    /// <summary>
    /// Attempt to recover from checkpoint.
    /// </summary>
    public async Task<RecoveryResult> TryRecoverAsync(string executionId, CancellationToken ct = default)
    {
        var checkpoint = await _store.LoadCheckpointAsync(executionId, ct);

        if (checkpoint == null)
        {
            return new RecoveryResult
            {
                CanResume = false,
                RecoveryMessage = "No checkpoint found for execution"
            };
        }

        // Find the last completed task and determine resume point
        var completedResults = new Dictionary<string, string>();
        string? resumeTaskId = null;

        foreach (var (taskId, taskCp) in checkpoint.TaskCheckpoints)
        {
            if (taskCp.Phase == "Completed" && taskCp.Result != null)
            {
                completedResults[taskId] = taskCp.Result;
            }
            else if (taskCp.Phase == "Started" || taskCp.Phase == "Decomposed")
            {
                // This task was in progress - resume from here
                resumeTaskId ??= taskId;
            }
        }

        _logger?.LogInformation(
            "[CHECKPOINT] Recovery possible for {ExecutionId}: {Completed} tasks completed, resume from {ResumeTask}",
            executionId, completedResults.Count, resumeTaskId ?? "root");

        return new RecoveryResult
        {
            CanResume = true,
            Checkpoint = checkpoint,
            ResumableTaskId = resumeTaskId,
            CompletedResults = completedResults,
            RecoveryMessage = $"Can resume from {resumeTaskId ?? "root"}, {completedResults.Count} tasks already completed"
        };
    }

    /// <summary>
    /// List all recoverable executions.
    /// </summary>
    public Task<IReadOnlyList<string>> ListRecoverableExecutionsAsync(CancellationToken ct = default) =>
        _store.ListCheckpointsAsync(ct);

    // ============================================================
    //  Internal Helpers
    // ============================================================

    private void UpdateParentSubtaskResult(string taskId, string? result)
    {
        if (result == null || _currentCheckpoint == null) return;

        // Find parent task (task ID format: parent:child)
        var lastColon = taskId.LastIndexOf(':');
        if (lastColon <= 0) return;

        var parentId = taskId[..lastColon];
        var childId = taskId[(lastColon + 1)..];

        if (_currentCheckpoint.TaskCheckpoints.TryGetValue(parentId, out var parentCp))
        {
            var subtaskResults = new Dictionary<string, string>(parentCp.SubtaskResults)
            {
                [childId] = result
            };

            var pendingSubtasks = parentCp.PendingSubtasks.Where(s => s != childId).ToList();

            _currentCheckpoint.TaskCheckpoints[parentId] = parentCp with
            {
                SubtaskResults = subtaskResults,
                PendingSubtasks = pendingSubtasks
            };
        }
    }

    private async Task PersistCheckpointAsync(CancellationToken ct)
    {
        ExecutionCheckpoint? checkpoint;
        lock (_checkpointLock)
        {
            checkpoint = _currentCheckpoint;
        }

        if (checkpoint != null)
        {
            await _store.SaveCheckpointAsync(checkpoint with
            {
                CheckpointedAt = DateTime.UtcNow
            }, ct);
        }
    }
}

