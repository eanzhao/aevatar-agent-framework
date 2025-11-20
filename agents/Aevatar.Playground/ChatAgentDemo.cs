using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Chat;

namespace Aevatar.Playground;

public class ChatAgentDemo : AgentDemoBase
{
    public override async Task RunAsync(IGAgentActorFactory factory, ILogger<Program> logger)
    {
        var actor = await factory.CreateGAgentActorAsync<ChatAgent>();

        logger.LogInformation("Type your message and press Enter. Type 'exit' to quit.");

        while (true)
        {
            // Simple prompt to indicate readiness
            Console.Write("> ");

            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input) || input.Trim().ToLower() == "exit")
            {
                break;
            }

            await actor.PublishEventAsync(new UserMessageEvent
            {
                UserId = "console_user",
                Message = input
            });

            // Wait a bit to avoid prompt overlapping with immediate logs
            await Task.Delay(100);
        }
    }
}