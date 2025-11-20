using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Microsoft.Extensions.Logging;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Chat;

public class ChatAgent : AIGAgentBase<ChatState, ChatConfig>
{
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // Initialize State
        if (string.IsNullOrEmpty(State.Id))
        {
            State.Id = Id.ToString();
            State.InteractionCount = 0;
        }
        State.LastActiveAt = Timestamp.FromDateTime(DateTime.UtcNow);

        // Initialize Config Defaults
        if (string.IsNullOrEmpty(Config.WelcomeMessage))
        {
            Config.WelcomeMessage = "Hello! I am your Aevatar Chat Agent.";
        }
        if (string.IsNullOrEmpty(Config.Persona))
        {
            Config.Persona = "helpful assistant";
        }

        // Configure System Prompt based on Config
        SystemPrompt = $"You are a {Config.Persona}. {Config.WelcomeMessage}";
        Logger.LogInformation("ChatAgent {AgentId} activated with persona {Persona}", Id, Config.Persona);

        await InitializeAsync("deepseek", cancellationToken: ct);
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleUserMessage(UserMessageEvent evt)
    {
        Logger.LogInformation("Received message from {UserId}: {Message}", evt.UserId, evt.Message);

        // Update State
        State.InteractionCount++;
        State.LastActiveAt = Timestamp.FromDateTime(DateTime.UtcNow);

        // Use AI Capability (AIGAgentBase feature) with Streaming
        var request = new ChatRequest 
        { 
            Message = evt.Message,
            RequestId = Guid.NewGuid().ToString()
        };

        var fullResponse = new System.Text.StringBuilder();
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"\nAI > ");
        
        await foreach (var token in ChatStreamAsync(request))
        {
            Console.Write(token);
            fullResponse.Append(token);
        }
        
        Console.WriteLine();
        Console.ResetColor();

        var responseContent = fullResponse.ToString();
        Logger.LogInformation("AI Response: {Response}", responseContent);

        // Publish Response
        await PublishAsync(new AgentResponseEvent
        {
            UserId = evt.UserId,
            Response = responseContent,
            AgentId = Id.ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Chat Agent ({State.InteractionCount} interactions)");
    }
}

