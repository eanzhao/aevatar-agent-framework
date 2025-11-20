namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Agent that throws errors in GetDescriptionAsync for testing error handling
/// </summary>
public class ErrorDescriptionAgent : GAgentBase<TestAgentState>
{
    public bool ShouldThrowInGetDescription { get; set; }
    public string ErrorMessage { get; set; } = "GetDescriptionAsync failed";
    public Type ExceptionType { get; set; } = typeof(InvalidOperationException);
    
    public override async Task<string> GetDescriptionAsync()
    {
        if (ShouldThrowInGetDescription)
        {
            await Task.Delay(10); // Simulate async work
            
            if (ExceptionType == typeof(InvalidOperationException))
            {
                throw new InvalidOperationException(ErrorMessage);
            }
            else if (ExceptionType == typeof(NotImplementedException))
            {
                throw new NotImplementedException(ErrorMessage);
            }
            else if (ExceptionType == typeof(TimeoutException))
            {
                throw new TimeoutException(ErrorMessage);
            }
            else
            {
                throw new Exception(ErrorMessage);
            }
        }
        
        return await base.GetDescriptionAsync();
    }
    
    public override string GetDescription()
    {
        if (ShouldThrowInGetDescription)
        {
            throw new InvalidOperationException($"Sync: {ErrorMessage}");
        }
        return $"ErrorDescriptionAgent: {State.Name}";
    }
}


