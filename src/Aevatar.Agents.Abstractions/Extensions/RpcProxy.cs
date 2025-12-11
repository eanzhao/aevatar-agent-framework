using System.Reflection;
using Aevatar.Agents.Abstractions.Rpc;
using Aevatar.Agents.Rpc;
using Google.Protobuf;

namespace Aevatar.Agents.Abstractions.Extensions;

/// <summary>
/// Dynamic proxy for type-safe RPC calls through interface.
/// Automatically detects runtime type:
/// - Local Runtime: Direct method invocation (zero overhead)
/// - Orleans/Remote Runtime: RPC via Protobuf serialization
/// </summary>
/// <typeparam name="TInterface">The agent interface type</typeparam>
public class RpcProxy<TInterface> : DispatchProxy where TInterface : class
{
    private IGAgentActor _actor = null!;
    private TInterface? _directAgent;  // For local runtime direct calls
    private bool _isLocalRuntime;

    /// <summary>
    /// Create a proxy instance for the given actor
    /// </summary>
    public static TInterface Create(IGAgentActor actor)
    {
        var proxy = Create<TInterface, RpcProxy<TInterface>>() as RpcProxy<TInterface>;
        proxy!._actor = actor;
        
        // Try to get direct agent reference (works for Local runtime)
        try
        {
            var agent = actor.GetAgent();
            if (agent is TInterface typedAgent)
            {
                proxy._directAgent = typedAgent;
                proxy._isLocalRuntime = true;
            }
        }
        catch (NotSupportedException)
        {
            // Orleans/Remote runtime - GetAgent() throws, use RPC
            proxy._isLocalRuntime = false;
        }
        
        return (proxy as TInterface)!;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod == null)
            throw new ArgumentNullException(nameof(targetMethod));

        // Local runtime: Direct call (zero overhead)
        if (_isLocalRuntime && _directAgent != null)
        {
            return targetMethod.Invoke(_directAgent, args);
        }

        // Remote runtime: RPC via Protobuf
        return InvokeViaRpc(targetMethod, args);
    }

    private object? InvokeViaRpc(MethodInfo targetMethod, object?[]? args)
    {
        // Build RPC request
        var request = new RpcRequest
        {
            MethodName = targetMethod.Name,
            CorrelationId = Guid.NewGuid().ToString()
        };

        foreach (var arg in args ?? Array.Empty<object?>())
        {
            request.Args.Add(ProtobufPacker.Pack(arg));
        }

        // Get return type
        var returnType = targetMethod.ReturnType;
        
        // Handle Task and Task<T>
        if (returnType == typeof(Task))
        {
            return InvokeVoidAsync(request);
        }
        
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var method = typeof(RpcProxy<TInterface>)
                .GetMethod(nameof(InvokeAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(resultType);
            return method.Invoke(this, [request]);
        }

        throw new NotSupportedException($"Return type {returnType} is not supported. Only Task and Task<T> are allowed.");
    }

    private async Task InvokeVoidAsync(RpcRequest request)
    {
        var responseBytes = await _actor.InvokeRpcAsync(request.ToByteArray());
        var response = RpcResponse.Parser.ParseFrom(responseBytes);

        if (!response.Success)
        {
            throw new InvalidOperationException(
                $"RPC call failed: {response.Error?.Message ?? "Unknown error"}");
        }
    }

    private async Task<TResult> InvokeAsync<TResult>(RpcRequest request)
    {
        var responseBytes = await _actor.InvokeRpcAsync(request.ToByteArray());
        var response = RpcResponse.Parser.ParseFrom(responseBytes);

        if (!response.Success)
        {
            throw new InvalidOperationException(
                $"RPC call failed: {response.Error?.Message ?? "Unknown error"}");
        }

        return ProtobufPacker.Unpack<TResult>(response.Result);
    }
}

/// <summary>
/// Extension methods to create RPC proxy from IGAgentActor
/// </summary>
public static class RpcProxyExtensions
{
    /// <summary>
    /// Create a type-safe proxy for RPC calls through the specified interface
    /// </summary>
    /// <typeparam name="TInterface">The agent interface (must inherit from IGAgent)</typeparam>
    /// <param name="actor">The actor to proxy</param>
    /// <returns>A proxy implementing TInterface that forwards calls via RPC</returns>
    /// <example>
    /// var bankAgent = actor.As&lt;IBankAccountAgent&gt;();
    /// await bankAgent.CreateAccountAsync("User", 1000m);
    /// var balance = await bankAgent.GetBalanceAsync();
    /// </example>
    public static TInterface As<TInterface>(this IGAgentActor actor) where TInterface : class
    {
        if (!typeof(TInterface).IsInterface)
        {
            throw new ArgumentException($"{typeof(TInterface).Name} must be an interface");
        }

        return RpcProxy<TInterface>.Create(actor);
    }
}

