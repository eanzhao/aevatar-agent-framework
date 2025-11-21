using System.Text;

namespace Aevatar.Agents.Core.Helpers;

/// <summary>
/// Helper class for formatting exceptions.
/// </summary>
public static class ExceptionFormatter
{
    /// <summary>
    /// Build a complete exception message including all inner exceptions.
    /// </summary>
    public static string BuildFullExceptionMessage(Exception exception)
    {
        var messages = new List<string>();
        var currentException = exception;
        var level = 0;
        
        while (currentException != null && level < 10) // Limit depth to prevent infinite loops
        {
            if (level == 0)
            {
                messages.Add($"{currentException.GetType().Name}: {currentException.Message}");
            }
            else
            {
                messages.Add($" ---> {currentException.GetType().Name}: {currentException.Message}");
            }
            
            currentException = currentException.InnerException;
            level++;
        }
        
        if (level >= 10 && currentException != null)
        {
            messages.Add(" ---> [Exception chain truncated]");
        }
        
        return string.Join(Environment.NewLine, messages);
    }
}
