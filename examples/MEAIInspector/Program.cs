using Microsoft.Extensions.AI;
using System.Reflection;

var response = new ChatResponse(new List<ChatMessage>
{
    new ChatMessage(ChatRole.Assistant, "test")
});

Console.WriteLine("ChatResponse properties:");
foreach (var prop in typeof(ChatResponse).GetProperties())
{
    Console.WriteLine($"- {prop.Name}: {prop.PropertyType.Name}");
}

Console.WriteLine("\nChatMessage properties:");
foreach (var prop in typeof(ChatMessage).GetProperties())
{
    Console.WriteLine($"- {prop.Name}: {prop.PropertyType.Name}");
}
