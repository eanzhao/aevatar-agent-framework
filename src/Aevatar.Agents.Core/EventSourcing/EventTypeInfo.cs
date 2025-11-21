using System;
using Google.Protobuf;

namespace Aevatar.Agents.Core.EventSourcing;

public record EventTypeInfo(Type Type, MessageParser Parser);
