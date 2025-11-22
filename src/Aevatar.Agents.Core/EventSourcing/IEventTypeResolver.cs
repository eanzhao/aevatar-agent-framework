using System.Reflection;

namespace Aevatar.Agents.Core.EventSourcing;

public interface IEventTypeResolver
{
    EventTypeInfo? Resolve(string typeUrl, Assembly searchAssembly);
}
