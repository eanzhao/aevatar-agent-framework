using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using ChatRoomDemo;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace ChatRoomDemo.Agents;

/// <summary>
/// Chat user agent that represents a participant in a chat room.
/// </summary>
public class ChatUserAgent : GAgentBase<ChatUserState>
{
    private DateTime _lastMessageTime = DateTime.UtcNow;
    private readonly TimeSpan _rateLimitWindow = TimeSpan.FromSeconds(1);
    
    public ChatUserAgent() : base()
    {
    }

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        // Initialize user state properties (State object already exists)
        State.UserName = $"User_{Id.ToString("N").Substring(0, 8)}";
        State.JoinedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        State.MessageCount = 0;
        State.IsModerator = false;
        
        Logger.LogInformation("Chat user {UserName} activated", State.UserName);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"User: {State.UserName} (Messages: {State.MessageCount})");
    }

    public async Task JoinRoomAsync(string roomId)
    {
        State.RoomId = roomId;
        State.JoinedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        
        // Notify room of join
        var joinEvent = new UserJoinedEvent
        {
            UserId = Id.ToString(),
            UserName = State.UserName,
            RoomId = roomId,
            Timestamp = State.JoinedAt
        };
        
        await PublishAsync(joinEvent, EventDirection.Up);
        Logger.LogInformation("User {UserName} joined room {RoomId}", State.UserName, roomId);
    }

    public async Task LeaveRoomAsync()
    {
        if (string.IsNullOrEmpty(State.RoomId))
        {
            Logger.LogWarning("User {UserName} is not in a room", State.UserName);
            return;
        }
        
        var leaveEvent = new UserLeftEvent
        {
            UserId = Id.ToString(),
            UserName = State.UserName,
            RoomId = State.RoomId,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        
        await PublishAsync(leaveEvent, EventDirection.Up);
        Logger.LogInformation("User {UserName} left room {RoomId}", State.UserName, State.RoomId);
        
        State.RoomId = string.Empty;
    }

    public async Task SendMessageAsync(string message, bool isPrivate = false, string? targetUserId = null)
    {
        // Rate limiting
        var now = DateTime.UtcNow;
        if (now - _lastMessageTime < _rateLimitWindow)
        {
            Logger.LogWarning("User {UserName} rate limited", State.UserName);
            return;
        }
        _lastMessageTime = now;
        
        State.MessageCount++;
        
        var chatMessage = new ChatMessageEvent
        {
            UserId = Id.ToString(),
            UserName = State.UserName,
            Message = message,
            RoomId = State.RoomId,
            Timestamp = Timestamp.FromDateTime(now),
            IsPrivate = isPrivate,
            TargetUserId = targetUserId ?? string.Empty
        };
        
        // Send to parent (room)
        await PublishAsync(chatMessage, EventDirection.Up);
        Logger.LogDebug("User {UserName} sent message: {Message}", State.UserName, message);
    }

    public async Task SetTypingAsync(bool isTyping)
    {
        var typingEvent = new UserTypingEvent
        {
            UserId = Id.ToString(),
            UserName = State.UserName,
            IsTyping = isTyping,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        
        await PublishAsync(typingEvent, EventDirection.Up);
    }

    [EventHandler]
    public Task HandleRoomAnnouncement(RoomAnnouncementEvent evt)
    {
        var priorityIcon = evt.Priority switch
        {
            0 => "ℹ️",
            1 => "⚠️",
            2 => "🚨",
            _ => "📢"
        };
        
        Logger.LogInformation("{Icon} Room announcement for {UserName}: {Message}", 
            priorityIcon, State.UserName, evt.Message);
        
        // In a real application, this would update the UI
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleChatMessage(ChatMessageEvent evt)
    {
        // Filter private messages
        if (evt.IsPrivate && evt.TargetUserId != Id.ToString() && evt.UserId != Id.ToString())
        {
            return Task.CompletedTask;
        }
        
        // Check if user is muted
        if (State.MutedUsers.Contains(evt.UserId))
        {
            Logger.LogDebug("Ignoring message from muted user {UserId}", evt.UserId);
            return Task.CompletedTask;
        }
        
        var messagePrefix = evt.IsPrivate ? "[Private] " : "";
        Logger.LogInformation("{Prefix}{UserName} sees message from {Sender}: {Message}", 
            messagePrefix, State.UserName, evt.UserName, evt.Message);
        
        // In a real application, this would update the UI
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleUserTyping(UserTypingEvent evt)
    {
        // Don't show own typing indicator
        if (evt.UserId == Id.ToString())
        {
            return Task.CompletedTask;
        }
        
        var status = evt.IsTyping ? "is typing..." : "stopped typing";
        Logger.LogDebug("{UserName} sees: {OtherUser} {Status}", 
            State.UserName, evt.UserName, status);
        
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleUserBanned(UserBannedEvent evt)
    {
        if (evt.UserId == Id.ToString())
        {
            Logger.LogWarning("User {UserName} has been banned!", State.UserName);
            // Force leave room
            State.RoomId = string.Empty;
        }
        
        return Task.CompletedTask;
    }

    public Task MuteUserAsync(string userId)
    {
        if (!State.MutedUsers.Contains(userId))
        {
            State.MutedUsers.Add(userId);
            Logger.LogInformation("User {UserName} muted user {UserId}", State.UserName, userId);
        }
        return Task.CompletedTask;
    }

    public Task UnmuteUserAsync(string userId)
    {
        State.MutedUsers.Remove(userId);
        Logger.LogInformation("User {UserName} unmuted user {UserId}", State.UserName, userId);
        return Task.CompletedTask;
    }

    public async Task BanUserAsync(string targetUserId, string targetUserName, string reason)
    {
        if (!State.IsModerator)
        {
            Logger.LogWarning("Non-moderator {UserName} attempted to ban user", State.UserName);
            return;
        }
        
        var banEvent = new UserBannedEvent
        {
            UserId = targetUserId,
            UserName = targetUserName,
            BannedBy = State.UserName,
            Reason = reason,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        
        await PublishAsync(banEvent, EventDirection.Up);
        Logger.LogInformation("Moderator {UserName} banned user {TargetUser}: {Reason}", 
            State.UserName, targetUserName, reason);
    }
}
