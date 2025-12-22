namespace Aevatar.Agents.Abstractions.Helpers;

/// <summary>
/// AgentId 规范化工具。
///
/// 核心约定（强约束）：
/// - **系统对外唯一标识**使用完整格式：<c>"AgentTypeShortName:RawId"</c>
/// - 创建时允许传入 RawId（通常是 Guid string），系统会自动组装成完整 ActorId
/// - 后续所有 Manager/Stream/Hierarchy 操作都应使用完整 ActorId（即 <see cref="IGAgent.Id"/> / <see cref="IGAgentActor.Id"/>）
///
/// WHY:
/// - Orleans GrainKey/StreamKey 必须包含类型信息，否则同 RawId 不同类型会发生冲突
/// - 统一格式后，Local/Proto/Orleans 三个运行时的 ID 行为一致
/// </summary>
public static class AgentId
{
    public const char Separator = ':';

    /// <summary>
    /// 从 <see cref="Type"/> 获取 AgentType 的短名（不含命名空间，不含泛型 arity）。
    /// </summary>
    public static string GetAgentTypeShortName(Type agentType)
    {
        ArgumentNullException.ThrowIfNull(agentType);

        var name = agentType.Name;
        var tick = name.IndexOf('`');
        return tick > 0 ? name[..tick] : name;
    }

    /// <summary>
    /// 从程序集限定名/全名中提取 AgentType 的短名。
    ///
    /// 例如：
    /// - "Aevatar.App.Agents.UserQuotaGAgent, Aevatar.App.Agents" -> "UserQuotaGAgent"
    /// - "Aevatar.App.Agents.UserQuotaGAgent" -> "UserQuotaGAgent"
    /// </summary>
    public static string GetAgentTypeShortName(string agentTypeName)
    {
        if (string.IsNullOrWhiteSpace(agentTypeName))
            return string.Empty;

        var fullName = agentTypeName.Split(',')[0].Trim();
        var lastDot = fullName.LastIndexOf('.');
        var shortName = lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;

        var tick = shortName.IndexOf('`');
        return tick > 0 ? shortName[..tick] : shortName;
    }

    /// <summary>
    /// 将用户输入的 id（RawId 或 ActorId）规范化为 ActorId（"Type:RawId"）。
    ///
    /// 规则：
    /// - 若输入已是 "Type:RawId" 且 Type 匹配，则原样返回
    /// - 若输入不含分隔符，则视为 RawId，自动补齐前缀
    /// - 若输入含分隔符但 Type 不匹配，则直接抛异常（避免跨类型误用/双重拼接）
    /// </summary>
    public static string Normalize(Type agentType, string id)
    {
        ArgumentNullException.ThrowIfNull(agentType);
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("id cannot be null/empty.", nameof(id));
        }

        id = id.Trim();

        var typeShortName = GetAgentTypeShortName(agentType);
        var sep = id.IndexOf(Separator);
        if (sep < 0)
        {
            // RawId -> ActorId
            return $"{typeShortName}{Separator}{id}";
        }

        // Already looks like ActorId. Validate type prefix.
        var prefix = id[..sep];
        var rawId = sep < id.Length - 1 ? id[(sep + 1)..] : string.Empty;
        if (string.IsNullOrWhiteSpace(rawId))
        {
            throw new ArgumentException($"Invalid actor id '{id}': raw id part is empty.", nameof(id));
        }

        if (!string.Equals(prefix, typeShortName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Invalid actor id '{id}': type prefix '{prefix}' does not match '{typeShortName}'. " +
                "Pass a raw id (e.g., Guid string) or a correctly prefixed actor id.",
                nameof(id));
        }

        return id;
    }

    public static string Normalize<TAgent>(string id) where TAgent : IGAgent
        => Normalize(typeof(TAgent), id);

    /// <summary>
    /// 尝试拆分 ActorId -> (TypeShortName, RawId)。
    /// </summary>
    public static bool TrySplit(string actorId, out string agentTypeShortName, out string rawId)
    {
        agentTypeShortName = string.Empty;
        rawId = string.Empty;

        if (string.IsNullOrWhiteSpace(actorId))
            return false;

        var s = actorId.Trim();
        var idx = s.IndexOf(Separator);
        if (idx <= 0 || idx >= s.Length - 1)
            return false;

        agentTypeShortName = s[..idx];
        rawId = s[(idx + 1)..];
        return !string.IsNullOrWhiteSpace(agentTypeShortName) && !string.IsNullOrWhiteSpace(rawId);
    }

    /// <summary>
    /// 从 ActorId 提取 RawId。若输入不是 ActorId，则按 RawId 原样返回（trim 后）。
    /// </summary>
    public static string ExtractRawId(string idOrActorId)
    {
        if (TrySplit(idOrActorId, out _, out var raw))
            return raw;

        return string.IsNullOrWhiteSpace(idOrActorId) ? string.Empty : idOrActorId.Trim();
    }
}


