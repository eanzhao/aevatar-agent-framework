using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Chat;
using Aevatar.Agents.Runtime.Local;

// 1. Setup Container
var builder = Host.CreateApplicationBuilder(args);

// 1.0 Load Configuration
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true);
builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));

// 1.1 Add Logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

// 2. Use the Runtime Extension
builder.Services.AddAevatarLocalRuntime().AddMEAI();

var app = builder.Build();

// 2.1 Start the host (optional for pure console loop, but good practice for background services)
await app.StartAsync();

// 3. Get Actor Factory
var factory = app.Services.GetRequiredService<IGAgentActorFactory>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

// 4. Create Actor (The Wrapper)
var actor = await factory.CreateGAgentActorAsync<ChatAgent>();

// 5. Interact
logger.LogInformation("=== Aevatar Chat Playground ===");
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

logger.LogInformation("Playground terminated.");

await app.StopAsync();