# Chat Room Demo

A demonstration of the Aevatar Agent Framework's capabilities through a real-time chat room implementation. This demo showcases how agents can collaborate to create a distributed chat system that works across different runtime environments.

## Overview

This demo implements a chat room system using two types of agents:
- **ChatRoomAgent**: Manages a chat room, handles message broadcasting, and enforces rules
- **ChatUserAgent**: Represents individual users who can send messages, receive notifications, and interact with the room

## Features Demonstrated

### Agent Communication
- **Event-Driven Messaging**: All communication happens through typed Protobuf events
- **Directional Broadcasting**: Messages flow up to the room and down to participants
- **Private Messaging**: Direct user-to-user communication
- **Typing Indicators**: Real-time status updates

### Room Management
- **User Join/Leave**: Dynamic participant management
- **Topic Management**: Room moderators can change discussion topics
- **Auto-Moderation**: Automatic detection of spam and inappropriate content
- **Ban System**: Moderators can ban disruptive users

### Agent Hierarchy
```
ChatRoomAgent (Parent)
├── ChatUserAgent (Alice - Moderator)
├── ChatUserAgent (Bob)
├── ChatUserAgent (Charlie)
├── ChatUserAgent (Diana)
└── ChatUserAgent (Eve)
```

### Event Flow

1. **User → Room (UP)**:
   - User joins/leaves
   - Send messages
   - Typing status
   - Ban requests (moderators only)

2. **Room → Users (DOWN)**:
   - Broadcast messages
   - Room announcements
   - Private message delivery
   - Ban notifications

## Running the Demo

### Local Runtime (Default)
```bash
dotnet run
# or explicitly:
dotnet run -- local
```

### Orleans Runtime
```bash
dotnet run -- orleans
```

### ProtoActor Runtime
```bash
dotnet run -- protoactor
```

## Demo Scenario

The demo simulates a typical chat room session:

1. **Room Creation**: A main chat room is created
2. **Users Join**: Five users (Alice, Bob, Charlie, Diana, Eve) join the room
3. **Chat Activity**:
   - Alice (moderator) welcomes everyone
   - Bob responds with typing indicator
   - Charlie sends a private message to Diana
   - Diana responds publicly
   - Eve attempts to spam
4. **Moderation**:
   - Auto-moderator warns about spam
   - Alice changes the room topic
   - Alice bans Eve for spamming
5. **User Leaves**: Bob leaves the room
6. **Metrics**: Display final statistics

## Key Concepts

### Rate Limiting
Users are rate-limited to prevent spam (1 message per second in this demo).

### Muting System
Users can mute other users to stop seeing their messages locally.

### Moderator Privileges
- Change room topic
- Ban users
- Send high-priority announcements

### Message Types
- **Public Messages**: Broadcast to all room participants
- **Private Messages**: Delivered only to specific users
- **System Announcements**: Important room notifications
- **Typing Indicators**: Show who's currently typing

## Architecture Benefits

### Runtime Flexibility
The same chat room logic works on:
- **Local**: For development and testing
- **Orleans**: For scalable cloud deployment
- **ProtoActor**: For high-performance scenarios

### Scalability
- **Horizontal Scaling**: Add more hosts as user count grows
- **Load Distribution**: Users and rooms can be distributed across hosts
- **Fault Tolerance**: Orleans and ProtoActor provide resilience

### Event Sourcing Ready
All state changes happen through events, making it easy to:
- Replay chat history
- Implement audit logs
- Add persistence

## Extending the Demo

### Add Features
- **File Sharing**: Add file upload events
- **Voice Chat**: Integrate WebRTC signaling
- **Reactions**: Add emoji reactions to messages
- **Threads**: Implement threaded conversations

### Add Persistence
- Store chat history using event sourcing
- Implement user profiles with database backing
- Add room configuration persistence

### Add UI
- Create a web frontend using SignalR
- Build a mobile app with real-time updates
- Implement a CLI chat client

## Performance Considerations

### Message Batching
For high-volume scenarios, consider batching messages before broadcasting.

### Subscription Management
Clean up subscriptions when users leave to prevent memory leaks.

### Event Size
Keep event payloads small for better performance across network boundaries.

## Troubleshooting

### Users Not Receiving Messages
- Check parent-child relationships are properly set
- Verify AutoSubscribeToParent is true
- Ensure event direction is correct

### High Memory Usage
- Implement message history limits
- Clean up inactive user agents
- Use appropriate runtime for scale

### Network Issues (Orleans/ProtoActor)
- Check firewall settings
- Verify cluster configuration
- Ensure proper service discovery

## Learn More

This demo illustrates:
- How to build multi-agent systems
- Event-driven architecture patterns
- Runtime abstraction benefits
- Real-time communication patterns

For more examples, check out the other demos in the examples folder.
