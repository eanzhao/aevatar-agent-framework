using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.AI.Core.Abstractions;
using Aevatar.Agents.AI.Core.Messages;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.Core.Conversation;

/// <summary>
/// Implementation of IConversationManager for managing AI conversation history.
/// </summary>
public class ConversationManager : IConversationManager
{
    private readonly List<AevatarChatMessage> _messages;
    private readonly int _maxHistory;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the ConversationManager class.
    /// </summary>
    /// <param name="maxHistory">Maximum number of messages to retain in history. Default is 100.</param>
    public ConversationManager(int maxHistory = 100)
    {
        _maxHistory = maxHistory;
        _messages = new List<AevatarChatMessage>();
    }

    /// <inheritdoc />
    public void AddUserMessage(string message)
    {
        lock (_lock)
        {
            var chatMessage = new AevatarChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Role = AevatarChatRole.User,
                Content = message,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                TokenCount = EstimateTokenCount(message)
            };

            AddMessage(chatMessage);
        }
    }

    /// <inheritdoc />
    public void AddAssistantMessage(string message)
    {
        lock (_lock)
        {
            var chatMessage = new AevatarChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Role = AevatarChatRole.Assistant,
                Content = message,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                TokenCount = EstimateTokenCount(message)
            };

            AddMessage(chatMessage);
        }
    }

    /// <inheritdoc />
    public void AddSystemMessage(string message)
    {
        lock (_lock)
        {
            var chatMessage = new AevatarChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Role = AevatarChatRole.System,
                Content = message,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                TokenCount = EstimateTokenCount(message)
            };

            AddMessage(chatMessage);
        }
    }

    /// <inheritdoc />
    public void AddFunctionMessage(string functionName, string result)
    {
        lock (_lock)
        {
            var chatMessage = new AevatarChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Role = AevatarChatRole.Function,
                Content = result,
                FunctionName = functionName,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                TokenCount = EstimateTokenCount(result)
            };

            AddMessage(chatMessage);
        }
    }

    /// <inheritdoc />
    public List<AevatarChatMessage> GetHistory()
    {
        lock (_lock)
        {
            return new List<AevatarChatMessage>(_messages);
        }
    }

    /// <inheritdoc />
    public List<AevatarChatMessage> GetHistory(int maxMessages)
    {
        lock (_lock)
        {
            if (maxMessages >= _messages.Count)
            {
                return new List<AevatarChatMessage>(_messages);
            }

            // Return the most recent messages, but preserve system messages at the beginning
            var systemMessages = _messages.Where(m => m.Role == AevatarChatRole.System).ToList();
            var nonSystemMessages = _messages.Where(m => m.Role != AevatarChatRole.System).ToList();

            var messagesToReturn = new List<AevatarChatMessage>(systemMessages);
            var remainingSlots = Math.Max(0, maxMessages - systemMessages.Count);
            
            if (remainingSlots > 0 && nonSystemMessages.Count > 0)
            {
                var recentMessages = nonSystemMessages
                    .Skip(Math.Max(0, nonSystemMessages.Count - remainingSlots))
                    .Take(remainingSlots);
                messagesToReturn.AddRange(recentMessages);
            }

            return messagesToReturn;
        }
    }

    /// <inheritdoc />
    public void ClearHistory()
    {
        lock (_lock)
        {
            _messages.Clear();
        }
    }

    /// <inheritdoc />
    public int MessageCount
    {
        get
        {
            lock (_lock)
            {
                return _messages.Count;
            }
        }
    }

    /// <inheritdoc />
    public int EstimatedTokenCount
    {
        get
        {
            lock (_lock)
            {
                return _messages.Sum(m => m.TokenCount);
            }
        }
    }

    /// <inheritdoc />
    public void TrimToTokenLimit(int maxTokens, bool preserveSystemMessage = true)
    {
        lock (_lock)
        {
            if (EstimatedTokenCount <= maxTokens)
            {
                return;
            }

            var systemMessages = preserveSystemMessage
                ? _messages.Where(m => m.Role == AevatarChatRole.System).ToList()
                : new List<AevatarChatMessage>();

            var nonSystemMessages = _messages.Where(m => m.Role != AevatarChatRole.System).ToList();
            
            var systemTokens = systemMessages.Sum(m => m.TokenCount);
            var remainingTokenBudget = maxTokens - systemTokens;

            if (remainingTokenBudget <= 0)
            {
                _messages.Clear();
                _messages.AddRange(systemMessages);
                return;
            }

            // Keep the most recent messages that fit within the token budget
            var messagesToKeep = new List<AevatarChatMessage>();
            var currentTokenCount = 0;

            for (int i = nonSystemMessages.Count - 1; i >= 0; i--)
            {
                var message = nonSystemMessages[i];
                if (currentTokenCount + message.TokenCount <= remainingTokenBudget)
                {
                    messagesToKeep.Insert(0, message);
                    currentTokenCount += message.TokenCount;
                }
                else
                {
                    break;
                }
            }

            _messages.Clear();
            _messages.AddRange(systemMessages);
            _messages.AddRange(messagesToKeep);
        }
    }

    /// <inheritdoc />
    public string GetSummary()
    {
        lock (_lock)
        {
            if (_messages.Count == 0)
            {
                return "Empty conversation";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Conversation Summary ({_messages.Count} messages):");
            
            var userMessages = _messages.Count(m => m.Role == AevatarChatRole.User);
            var assistantMessages = _messages.Count(m => m.Role == AevatarChatRole.Assistant);
            var systemMessages = _messages.Count(m => m.Role == AevatarChatRole.System);
            var functionMessages = _messages.Count(m => m.Role == AevatarChatRole.Function);

            sb.AppendLine($"- User messages: {userMessages}");
            sb.AppendLine($"- Assistant messages: {assistantMessages}");
            sb.AppendLine($"- System messages: {systemMessages}");
            sb.AppendLine($"- Function messages: {functionMessages}");
            sb.AppendLine($"- Total tokens: {EstimatedTokenCount}");

            if (_messages.Count > 0)
            {
                var firstMessage = _messages.First();
                var lastMessage = _messages.Last();
                sb.AppendLine($"- Started: {firstMessage.Timestamp.ToDateTime():yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"- Last activity: {lastMessage.Timestamp.ToDateTime():yyyy-MM-dd HH:mm:ss}");
            }

            return sb.ToString();
        }
    }

    /// <inheritdoc />
    public string Export(string format = "json")
    {
        lock (_lock)
        {
            return format.ToLowerInvariant() switch
            {
                "json" => ExportAsJson(),
                "text" => ExportAsText(),
                "markdown" => ExportAsMarkdown(),
                _ => throw new ArgumentException($"Unsupported export format: {format}", nameof(format))
            };
        }
    }

    /// <inheritdoc />
    public void Import(string data, string format = "json")
    {
        lock (_lock)
        {
            switch (format.ToLowerInvariant())
            {
                case "json":
                    ImportFromJson(data);
                    break;
                default:
                    throw new ArgumentException($"Unsupported import format: {format}", nameof(format));
            }
        }
    }

    private void AddMessage(AevatarChatMessage message)
    {
        _messages.Add(message);

        // Trim to max history if necessary
        if (_messages.Count > _maxHistory)
        {
            // Preserve system messages
            var systemMessages = _messages.Where(m => m.Role == AevatarChatRole.System).ToList();
            var nonSystemMessages = _messages.Where(m => m.Role != AevatarChatRole.System).ToList();

            var messagesToKeep = systemMessages.Count;
            var remainingSlots = _maxHistory - messagesToKeep;

            if (remainingSlots > 0 && nonSystemMessages.Count > remainingSlots)
            {
                nonSystemMessages = nonSystemMessages
                    .Skip(nonSystemMessages.Count - remainingSlots)
                    .ToList();
            }

            _messages.Clear();
            _messages.AddRange(systemMessages);
            _messages.AddRange(nonSystemMessages);
        }
    }

    private int EstimateTokenCount(string text)
    {
        // Simple estimation: ~4 characters per token for English text
        // This is a rough approximation; real tokenization is model-specific
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    private string ExportAsJson()
    {
        var exportData = new
        {
            Version = "1.0",
            ExportedAt = DateTime.UtcNow,
            MessageCount = _messages.Count,
            EstimatedTokens = EstimatedTokenCount,
            Messages = _messages.Select(m => new
            {
                m.Id,
                Role = m.Role.ToString(),
                m.Content,
                m.FunctionName,
                m.FunctionArguments,
                Timestamp = m.Timestamp?.ToDateTime().ToString("O"),
                m.TokenCount,
                m.Metadata
            })
        };

        return JsonSerializer.Serialize(exportData, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private string ExportAsText()
    {
        var sb = new StringBuilder();
        foreach (var message in _messages)
        {
            var role = message.Role switch
            {
                AevatarChatRole.System => "SYSTEM",
                AevatarChatRole.User => "USER",
                AevatarChatRole.Assistant => "ASSISTANT",
                AevatarChatRole.Function => $"FUNCTION[{message.FunctionName}]",
                _ => "UNKNOWN"
            };

            sb.AppendLine($"[{message.Timestamp?.ToDateTime():yyyy-MM-dd HH:mm:ss}] {role}:");
            sb.AppendLine(message.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private string ExportAsMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Conversation Export");
        sb.AppendLine();
        sb.AppendLine($"**Exported**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Messages**: {_messages.Count}");
        sb.AppendLine($"**Estimated Tokens**: {EstimatedTokenCount}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var message in _messages)
        {
            var timestamp = message.Timestamp?.ToDateTime().ToString("HH:mm:ss") ?? "N/A";
            
            switch (message.Role)
            {
                case AevatarChatRole.System:
                    sb.AppendLine($"### 🔧 System Message ({timestamp})");
                    sb.AppendLine($"```");
                    sb.AppendLine(message.Content);
                    sb.AppendLine($"```");
                    break;
                    
                case AevatarChatRole.User:
                    sb.AppendLine($"### 👤 User ({timestamp})");
                    sb.AppendLine(message.Content);
                    break;
                    
                case AevatarChatRole.Assistant:
                    sb.AppendLine($"### 🤖 Assistant ({timestamp})");
                    sb.AppendLine(message.Content);
                    break;
                    
                case AevatarChatRole.Function:
                    sb.AppendLine($"### ⚡ Function Call: {message.FunctionName} ({timestamp})");
                    sb.AppendLine($"**Result**:");
                    sb.AppendLine($"```json");
                    sb.AppendLine(message.Content);
                    sb.AppendLine($"```");
                    break;
            }
            
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private void ImportFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        _messages.Clear();

        if (root.TryGetProperty("Messages", out var messagesElement))
        {
            foreach (var messageElement in messagesElement.EnumerateArray())
            {
                var message = new AevatarChatMessage
                {
                    Id = messageElement.GetProperty("Id").GetString() ?? Guid.NewGuid().ToString(),
                    Content = messageElement.GetProperty("Content").GetString() ?? string.Empty,
                };

                if (messageElement.TryGetProperty("Role", out var roleElement))
                {
                    var roleString = roleElement.GetString();
                    message.Role = roleString switch
                    {
                        var s when s?.Contains("SYSTEM") == true => AevatarChatRole.System,
                        var s when s?.Contains("USER") == true => AevatarChatRole.User,
                        var s when s?.Contains("ASSISTANT") == true => AevatarChatRole.Assistant,
                        var s when s?.Contains("FUNCTION") == true => AevatarChatRole.Function,
                        _ => AevatarChatRole.Unspecified
                    };
                }

                if (messageElement.TryGetProperty("FunctionName", out var funcNameElement))
                {
                    message.FunctionName = funcNameElement.GetString() ?? string.Empty;
                }

                if (messageElement.TryGetProperty("Timestamp", out var timestampElement))
                {
                    if (DateTime.TryParse(timestampElement.GetString(), out var timestamp))
                    {
                        message.Timestamp = Timestamp.FromDateTime(timestamp.ToUniversalTime());
                    }
                }

                if (messageElement.TryGetProperty("TokenCount", out var tokenElement))
                {
                    message.TokenCount = tokenElement.GetInt32();
                }
                else
                {
                    message.TokenCount = EstimateTokenCount(message.Content);
                }

                _messages.Add(message);
            }
        }
    }
}
