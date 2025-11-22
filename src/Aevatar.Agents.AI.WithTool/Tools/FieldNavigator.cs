using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aevatar.Agents.AI.WithTool.Tools;

/// <summary>
/// 字段导航器
/// 提供反射和 JSON Path 导航功能
/// </summary>
public static class FieldNavigator
{
    /// <summary>
    /// 通过反射获取字段值，支持 JSON Path
    /// </summary>
    public static object? GetFieldValue(
        object obj,
        string? fieldName,
        string? path,
        ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(fieldName))
        {
            return obj;
        }

        try
        {
            object? fieldValue = null;
            var type = obj.GetType();

            // 尝试获取属性
            var property = type.GetProperty(fieldName);
            if (property != null)
            {
                fieldValue = property.GetValue(obj);
            }
            else
            {
                // 尝试获取字段
                var field = type.GetField(fieldName);
                if (field != null)
                {
                    fieldValue = field.GetValue(obj);
                }
                // 如果是字典类型
                else if (obj is IDictionary<string, object> dict)
                {
                    dict.TryGetValue(fieldName, out fieldValue);
                }
            }

            // 如果找到了字段值并且有 JSON Path，使用 JSON Path 导航
            if (fieldValue != null && !string.IsNullOrEmpty(path))
            {
                return NavigateJsonPath(fieldValue, path, logger);
            }

            if (fieldValue == null)
            {
                logger?.LogWarning("Field {FieldName} not found in type {TypeName}",
                    fieldName, type.Name);
            }

            return fieldValue;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error getting field value for {FieldName}", fieldName);
            return null;
        }
    }

    /// <summary>
    /// 使用 JSON Path 导航对象
    /// </summary>
    public static object? NavigateJsonPath(
        object obj,
        string path,
        ILogger? logger = null)
    {
        try
        {
            // 将对象转换为 JSON
            var json = JsonSerializer.Serialize(obj);
            var jsonNode = JsonNode.Parse(json);

            if (jsonNode == null)
            {
                return null;
            }

            // 简化的 JSON Path 支持
            var result = NavigateJsonNode(jsonNode, path);

            if (result == null)
            {
                logger?.LogDebug("JSON Path {Path} returned no results", path);
                return null;
            }

            // 根据结果类型返回适当的值
            return ConvertJsonNodeToObject(result);
        }
        catch (JsonException ex)
        {
            logger?.LogError(ex, "Error parsing JSON path {Path}", path);
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Unexpected error navigating JSON path {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// 导航 JsonNode
    /// </summary>
    private static JsonNode? NavigateJsonNode(JsonNode node, string path)
    {
        // 移除前缀的 $ 或 $.
        if (path.StartsWith("$."))
        {
            path = path.Substring(2);
        }
        else if (path.StartsWith("$"))
        {
            path = path.Substring(1);
        }

        // 如果路径为空，返回当前节点
        if (string.IsNullOrEmpty(path))
        {
            return node;
        }

        var currentNode = node;
        var segments = ParsePathSegments(path);

        foreach (var segment in segments)
        {
            if (currentNode == null)
            {
                return null;
            }

            // 处理数组索引
            if (segment.StartsWith("[") && segment.EndsWith("]"))
            {
                if (currentNode is JsonArray array)
                {
                    var indexStr = segment.Substring(1, segment.Length - 2);
                    if (int.TryParse(indexStr, out var index) && index >= 0 && index < array.Count)
                    {
                        currentNode = array[index];
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }
            // 处理对象属性
            else
            {
                if (currentNode is JsonObject obj && obj.TryGetPropertyValue(segment, out var value))
                {
                    currentNode = value;
                }
                else
                {
                    return null;
                }
            }
        }

        return currentNode;
    }

    /// <summary>
    /// 解析路径段
    /// </summary>
    private static List<string> ParsePathSegments(string path)
    {
        var segments = new List<string>();
        var current = "";
        var inBracket = false;

        for (int i = 0; i < path.Length; i++)
        {
            var ch = path[i];

            if (ch == '[')
            {
                if (!string.IsNullOrEmpty(current))
                {
                    segments.Add(current);
                    current = "";
                }

                inBracket = true;
                current += ch;
            }
            else if (ch == ']')
            {
                current += ch;
                if (inBracket)
                {
                    segments.Add(current);
                    current = "";
                    inBracket = false;
                }
            }
            else if (ch == '.' && !inBracket)
            {
                if (!string.IsNullOrEmpty(current))
                {
                    segments.Add(current);
                    current = "";
                }
            }
            else
            {
                current += ch;
            }
        }

        if (!string.IsNullOrEmpty(current))
        {
            segments.Add(current);
        }

        return segments;
    }

    /// <summary>
    /// 将 JsonNode 转换为对象
    /// </summary>
    private static object? ConvertJsonNodeToObject(JsonNode node)
    {
        return node switch
        {
            JsonObject obj => obj.Deserialize<Dictionary<string, object>>(),
            JsonArray arr => arr.Deserialize<List<object>>(),
            JsonValue val => val.GetValueKind() switch
            {
                JsonValueKind.String => val.GetValue<string>(),
                JsonValueKind.Number => val.TryGetValue<long>(out var longVal)
                    ? longVal
                    : val.GetValue<double>(),
                JsonValueKind.True or JsonValueKind.False => val.GetValue<bool>(),
                JsonValueKind.Null => null,
                _ => val.ToString()
            },
            _ => node?.ToString()
        };
    }
}