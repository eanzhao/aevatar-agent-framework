using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using ChatRoomDemo;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace ChatRoomDemo.Agents;

/// <summary>
/// Chat room agent that manages a chat room and broadcasts messages to participants.
/// </summary>
public class ChatRoomAgent : GAgentBase<ChatRoomState>
{
    public ChatRoomAgent() : base()
    {
    }

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        // Initialize room state properties (State object already exists)
        State.RoomId = Id.ToString();
        State.RoomName = $"Room_{Id.ToString("N").Substring(0, 8)}";
        State.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        State.ParticipantCount = 0;
        State.TotalMessages = 0;
        State.Topic = "General Discussion";
        
        Logger.LogInformation("Chat room {RoomName} activated", State.RoomName);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Chat Room: {State.RoomName} ({State.ParticipantCount} participants)");
    }

    [EventHandler]
    public async Task HandleUserJoined(UserJoinedEvent evt)
    {
        Logger.LogInformation("User {UserName} joined room {RoomName}", evt.UserName, State.RoomName);
        
        // Update state
        State.Participants.Add(evt.UserId);
        State.ParticipantCount = State.Participants.Count;
        
        // Broadcast to all participants (children)
        var announcement = new RoomAnnouncementEvent
        {
            Message = $"{evt.UserName} has joined the room",
            AnnouncedBy = "System",
            Timestamp = evt.Timestamp,
            Priority = 0
        };
        
        await PublishAsync(announcement, EventDirection.Down);
        
        // Send welcome message to the new user
        var welcome = new ChatMessageEvent
        {
            UserId = "system",
            UserName = "System",
            Message = $"Welcome to {State.RoomName}, {evt.UserName}! Current topic: {State.Topic}",
            RoomId = State.RoomId,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            IsPrivate = true,
            TargetUserId = evt.UserId
        };
        
        await PublishAsync(welcome, EventDirection.Down);
    }

    [EventHandler]
    public async Task HandleUserLeft(UserLeftEvent evt)
    {
        Logger.LogInformation("User {UserName} left room {RoomName}", evt.UserName, State.RoomName);
        
        // Update state
        State.Participants.Remove(evt.UserId);
        State.ParticipantCount = State.Participants.Count;
        
        // Broadcast to remaining participants
        var announcement = new RoomAnnouncementEvent
        {
            Message = $"{evt.UserName} has left the room",
            AnnouncedBy = "System",
            Timestamp = evt.Timestamp,
            Priority = 0
        };
        
        await PublishAsync(announcement, EventDirection.Down);
    }

    [EventHandler]
    public async Task HandleChatMessage(ChatMessageEvent evt)
    {
        // Check if user is banned
        if (State.BannedUsers.Contains(evt.UserId))
        {
            Logger.LogWarning("Banned user {UserId} attempted to send message", evt.UserId);
            return;
        }
        
        State.TotalMessages++;
        
        if (evt.IsPrivate && !string.IsNullOrEmpty(evt.TargetUserId))
        {
            Logger.LogDebug("Private message from {From} to {To}", evt.UserName, evt.TargetUserId);
            // Forward private message only to target user
            // In a real system, we'd route this to the specific user agent
            await PublishAsync(evt, EventDirection.Down);
        }
        else
        {
            Logger.LogInformation("Broadcasting message from {UserName} to room", evt.UserName);
            // Broadcast public message to all participants
            await PublishAsync(evt, EventDirection.Down);
        }
        
        // Check for moderation triggers (simple example)
        if (evt.Message.Contains("spam", StringComparison.OrdinalIgnoreCase))
        {
            var warning = new RoomAnnouncementEvent
            {
                Message = $"⚠️ {evt.UserName}, please avoid spamming",
                AnnouncedBy = "AutoModerator",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Priority = 1
            };
            await PublishAsync(warning, EventDirection.Down);
        }
    }

    [EventHandler]
    public async Task HandleUserTyping(UserTypingEvent evt)
    {
        // Broadcast typing indicator to other users
        await PublishAsync(evt, EventDirection.Down);
    }

    [EventHandler]
    public async Task HandleUserBanned(UserBannedEvent evt)
    {
        Logger.LogWarning("User {UserName} banned by {BannedBy}: {Reason}", 
            evt.UserName, evt.BannedBy, evt.Reason);
        
        State.BannedUsers.Add(evt.UserId);
        State.Participants.Remove(evt.UserId);
        State.ParticipantCount = State.Participants.Count;
        
        // Announce ban to room
        var announcement = new RoomAnnouncementEvent
        {
            Message = $"⛔ {evt.UserName} has been banned: {evt.Reason}",
            AnnouncedBy = evt.BannedBy,
            Timestamp = evt.Timestamp,
            Priority = 2
        };
        
        await PublishAsync(announcement, EventDirection.Down);
    }

    public Task<ChatRoomState> GetRoomStateAsync()
    {
        return Task.FromResult(State);
    }

    public async Task SetTopicAsync(string newTopic, string setBy)
    {
        var oldTopic = State.Topic;
        State.Topic = newTopic;
        
        var announcement = new RoomAnnouncementEvent
        {
            Message = $"Room topic changed from '{oldTopic}' to '{newTopic}' by {setBy}",
            AnnouncedBy = setBy,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Priority = 0
        };
        
        await PublishAsync(announcement, EventDirection.Down);
    }
}
