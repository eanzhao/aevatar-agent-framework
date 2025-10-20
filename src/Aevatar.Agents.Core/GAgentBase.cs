using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Core;

public abstract class AgentBase<TState> : IGAgent<TState> where TState : class, new()
{
    protected readonly TState _state = new();
    protected readonly List<IGAgent> _subAgents = new();
    protected readonly IServiceProvider _serviceProvider;
    protected readonly IGAgentFactory _factory;
    protected readonly IMessageSerializer _serializer;
    protected readonly List<EventEnvelope> _pendingEvents = new();

      protected AgentBase(IServiceProvider serviceProvider, IGAgentFactory factory, IMessageSerializer serializer)
      {
          _serviceProvider = serviceProvider;
          _factory = factory;
          _serializer = serializer;
      }

      public Guid Id { get; } = Guid.NewGuid();

      public abstract Task RegisterEventHandlersAsync(IMessageStream stream, CancellationToken ct = default);

      public virtual async Task AddSubAgentAsync<TSubAgent, TSubState>(CancellationToken ct = default) 
          where TSubAgent : IGAgent<TSubState> 
          where TSubState : class, new()
      {
          var subAgentActor = await _factory.CreateAgentAsync<TSubAgent, TSubState>(Guid.NewGuid(), ct);
          var subAgent = _serviceProvider.GetRequiredService<TSubAgent>();
          _subAgents.Add(subAgent);
          var evt = new EventEnvelope
          {
              Id = Guid.NewGuid().ToString(),
              Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
              Version = GetStateVersion() + 1,
              Payload = Any.Pack(new SubAgentAdded { SubAgentId = subAgent.Id.ToString() })
          };
          await RaiseEventAsync(evt, ct);
          // 这里我们需要传递IGAgentActor，而不是this (AgentBase)
          IGAgentActor thisActor = _serviceProvider.GetRequiredService<IGAgentActor>();
          await subAgentActor.SubscribeToParentStreamAsync(thisActor, ct);
      }

      public virtual async Task RemoveSubAgentAsync(Guid subAgentId, CancellationToken ct = default)
      {
          var subAgent = _subAgents.FirstOrDefault(a => a.Id == subAgentId);
          if (subAgent != null)
          {
              _subAgents.Remove(subAgent);
              var evt = new EventEnvelope
              {
                  Id = Guid.NewGuid().ToString(),
                  Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                  Version = GetStateVersion() + 1,
                  Payload = Any.Pack(new SubAgentRemoved { SubAgentId = subAgentId.ToString() })
              };
              await RaiseEventAsync(evt, ct);
          }
      }

      public async Task RaiseEventAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : class
      {
          var envelope = new EventEnvelope
          {
              Id = Guid.NewGuid().ToString(),
              Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
              Version = GetStateVersion() + 1,
              Payload = Any.Pack(evt as IMessage)
          };
          _pendingEvents.Add(envelope);
          await ApplyEventAsync(envelope, ct);
      }

      public abstract Task ApplyEventAsync(EventEnvelope evt, CancellationToken ct = default);

      public IReadOnlyList<EventEnvelope> GetPendingEvents() => _pendingEvents.AsReadOnly();

      protected long GetStateVersion()
      {
          return _state switch
          {
              LLMAgentState llmState => llmState.CurrentVersion,
              CodingAgentState codeState => codeState.CurrentVersion,
              _ => 0
          };
      }

      public IReadOnlyList<IGAgent> GetSubAgents() => _subAgents.AsReadOnly();
      public TState GetState() => _state;

      public async Task ProduceEventAsync(IMessage message, CancellationToken ct = default)
      {
          var stream = _serviceProvider.GetRequiredService<IMessageStream>();
          await stream.ProduceAsync(message, ct);
      }
  }