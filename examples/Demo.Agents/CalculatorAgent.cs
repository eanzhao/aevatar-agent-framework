using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

/// <summary>
/// 计算器Agent状态
/// </summary>
public class CalculatorAgentState
{
    public double LastResult { get; set; }
    public int OperationCount { get; set; }
    public List<string> History { get; set; } = new();
}

/// <summary>
/// 示例：计算器Agent
/// </summary>
public class CalculatorAgent : GAgentBase<CalculatorAgentState>
{
    public CalculatorAgent(Guid id, ILogger<CalculatorAgent>? logger = null)
        : base(id, logger)
    {
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Calculator Agent - Performs basic arithmetic operations");
    }

    /// <summary>
    /// 加法运算
    /// </summary>
    public async Task<double> AddAsync(double a, double b, CancellationToken ct = default)
    {
        var result = a + b;
        await RecordOperation($"{a} + {b} = {result}", result, ct);
        return result;
    }

    /// <summary>
    /// 减法运算
    /// </summary>
    public async Task<double> SubtractAsync(double a, double b, CancellationToken ct = default)
    {
        var result = a - b;
        await RecordOperation($"{a} - {b} = {result}", result, ct);
        return result;
    }

    /// <summary>
    /// 乘法运算
    /// </summary>
    public async Task<double> MultiplyAsync(double a, double b, CancellationToken ct = default)
    {
        var result = a * b;
        await RecordOperation($"{a} × {b} = {result}", result, ct);
        return result;
    }

    /// <summary>
    /// 除法运算
    /// </summary>
    public async Task<double> DivideAsync(double a, double b, CancellationToken ct = default)
    {
        if (Math.Abs(b) < 0.0001)
            throw new DivideByZeroException("除数不能为零");

        var result = a / b;
        await RecordOperation($"{a} ÷ {b} = {result}", result, ct);
        return result;
    }

    /// <summary>
    /// 获取计算历史
    /// </summary>
    public List<string> GetHistory() => _state.History;

    /// <summary>
    /// 获取上次结果
    /// </summary>
    public double GetLastResult() => _state.LastResult;

    private async Task RecordOperation(string operation, double result, CancellationToken ct)
    {
        _state.LastResult = result;
        _state.OperationCount++;
        _state.History.Add($"[{_state.OperationCount}] {operation}");

        Console.WriteLine($"[CalculatorAgent] 计算完成: {operation}");

        await Task.CompletedTask;
    }
}
