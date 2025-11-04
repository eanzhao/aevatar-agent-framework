using Orleans;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// 标准 Agent Grain 接口
/// 用于区分不同的 Grain 实现
/// </summary>
public interface IStandardGAgentGrain : IGAgentGrain
{
    // 标准 Grain 不需要额外的方法
    // 这个接口仅用于类型区分
}
