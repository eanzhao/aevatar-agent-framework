using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;

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
    public CalculatorAgent(
        IServiceProvider serviceProvider,
        IGAgentFactory factory,
        IMessageSerializer serializer)
        : base(serviceProvider, factory, serializer)
    {
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

    /// <summary>
    /// 注册事件处理器
    /// </summary>
    public override Task RegisterEventHandlersAsync(IMessageStream stream, CancellationToken ct = default)
    {
        // 简化示例：不需要事件处理
        return Task.CompletedTask;
    }

    /// <summary>
    /// 应用事件（用于事件溯源）
    /// </summary>
    public override Task ApplyEventAsync(EventEnvelope evt, CancellationToken ct = default)
    {
        // 简化示例：不需要事件重放逻辑
        return Task.CompletedTask;
    }

    private async Task RecordOperation(string operation, double result, CancellationToken ct)
    {
        _state.LastResult = result;
        _state.OperationCount++;
        _state.History.Add($"[{_state.OperationCount}] {operation}");

        Console.WriteLine($"[CalculatorAgent] 计算完成: {operation}");

        await Task.CompletedTask;
    }
}
