using System.Reflection;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core;

/// <summary>
/// Agent 基类（带事件类型约束）
/// 约束 Agent 只能处理特定基类的事件
/// </summary>
/// <typeparam name="TState">Agent 状态类型</typeparam>
/// <typeparam name="TEvent">事件基类型（所有事件必须继承此类型）</typeparam>
public abstract class GAgentBase<TState, TEvent> : GAgentBase<TState>
    where TState : class, new()
    where TEvent : class, IMessage
{
    protected GAgentBase(ILogger? logger = null) : base(logger)
    {
    }

    protected GAgentBase(Guid id, ILogger? logger = null) : base(id, logger)
    {
    }

    /// <summary>
    /// 发布事件（带类型约束）
    /// </summary>
    protected new async Task<string> PublishAsync<T>(
        T evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default)
        where T : TEvent
    {
        return await base.PublishAsync(evt, direction, ct);
    }

    /// <summary>
    /// 处理事件（带类型约束检查）
    /// </summary>
    public override async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        var handlers = GetEventHandlers();

        foreach (var handler in handlers)
        {
            try
            {
                if (!ShouldHandleEvent(handler, envelope))
                    continue;

                var paramType = handler.GetParameters()[0].ParameterType;

                // AllEventHandler - 直接传递 EventEnvelope
                if (handler.GetCustomAttribute<AllEventHandlerAttribute>() != null)
                {
                    await InvokeHandler(handler, envelope, ct);
                    continue;
                }

                // EventHandler - 解包并检查类型约束
                if (envelope.Payload != null)
                {
                    try
                    {
                        var unpackMethod = typeof(Google.Protobuf.WellKnownTypes.Any)
                            .GetMethod("Unpack", Type.EmptyTypes)
                            ?.MakeGenericMethod(paramType);

                        if (unpackMethod != null)
                        {
                            var message = unpackMethod.Invoke(envelope.Payload, null);

                            // 检查是否是 TEvent 的子类型
                            if (message != null &&
                                paramType.IsInstanceOfType(message) &&
                                typeof(TEvent).IsAssignableFrom(paramType))
                            {
                                await InvokeHandler(handler, message, ct);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogTrace(ex, "Failed to unpack event payload for handler {Handler}", handler.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling event in {Handler}", handler.Name);
            }
        }
    }

    /// <summary>
    /// 判断方法是否是有效的事件处理器（带 TEvent 约束）
    /// </summary>
    protected override bool IsEventHandlerMethod(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != 1) return false;

        var paramType = parameters[0].ParameterType;

        // [EventHandler] 标记的方法，参数必须是 TEvent 或其子类
        if (method.GetCustomAttribute<EventHandlerAttribute>() != null)
        {
            return typeof(TEvent).IsAssignableFrom(paramType);
        }

        // [AllEventHandler] 标记的方法，参数必须是 EventEnvelope
        if (method.GetCustomAttribute<AllEventHandlerAttribute>() != null)
        {
            return paramType == typeof(EventEnvelope);
        }

        // 默认处理器：方法名为 HandleAsync 或 Handle，参数是 TEvent 的子类
        if (method.Name is "HandleAsync" or "Handle")
        {
            return typeof(TEvent).IsAssignableFrom(paramType) && !paramType.IsAbstract;
        }

        return false;
    }
}

