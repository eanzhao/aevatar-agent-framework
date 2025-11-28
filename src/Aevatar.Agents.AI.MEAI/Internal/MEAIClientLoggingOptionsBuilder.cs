using System.ClientModel.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.MEAI.Internal;

internal static class MEAIClientLoggingOptionsBuilder
{
    public static ClientLoggingOptions Create(IServiceProvider serviceProvider)
    {
        var loggingOptions = new ClientLoggingOptions
        {
            LoggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>(),
            EnableLogging = true,
            EnableMessageLogging = true,
            EnableMessageContentLogging = true,
            MessageContentSizeLimit = 64 * 1024
        };

        loggingOptions.AllowedHeaderNames.Add("Content-Type");
        loggingOptions.AllowedHeaderNames.Add("Accept");
        loggingOptions.AllowedHeaderNames.Add("Content-Length");
        loggingOptions.AllowedHeaderNames.Add("x-ms-request-id");
        loggingOptions.AllowedQueryParameters.Add("api-version");

        return loggingOptions;
    }
}

