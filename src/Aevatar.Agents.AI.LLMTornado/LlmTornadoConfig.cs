using LlmTornado.Code;

namespace Aevatar.Agents.AI.LLMTornado;

public class LlmTornadoConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public LLmProviders Provider { get; set; } = LLmProviders.OpenAi;
}