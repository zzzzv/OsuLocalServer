using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OsuLocalServer.Lazer;

internal static class RealmConverter
{
    /// <summary>
    /// 将 Realm 对象列表转为可序列化的 Dictionary 列表。
    /// 每个顶层对象使用独立的循环引用跟踪，避免同一条对象链内重复展开。
    /// </summary>
    public static List<object> ToList(IEnumerable items, int depth, HashSet<string>? noExpandFields = null)
    {
        var result = new List<object>();
        foreach (var item in items)
        {
            if (item is null)
                continue;

            var visited = new ConditionalWeakTable<object, object>();
            result.Add(ConvertComplexValue(item.GetType(), item, depth, noExpandFields, visited));
        }
        return result;
    }

    public static Dictionary<string, object?> ToDict(object obj, int depth, HashSet<string>? noExpandFields = null,
        ConditionalWeakTable<object, object>? visited = null)
    {
        visited ??= new ConditionalWeakTable<object, object>();

        var dict = new Dictionary<string, object?>();
        var props = GetMappedProperties(obj.GetType());

        foreach (var prop in props)
        {
            var value = prop.GetValue(obj);
            if (value is null)
                continue;

            dict[prop.Name] = ConvertComplexValue(prop.PropertyType, value, depth, noExpandFields, visited, prop.Name);
        }

        return dict;
    }

    private static PropertyInfo[] GetMappedProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == type)
            .ToArray();
    }

    private static object ConvertComplexValue(Type propType, object value, int depth,
        HashSet<string>? noExpandFields, ConditionalWeakTable<object, object> visited,
        string? propertyName = null)
    {
        if (propertyName is not null && noExpandFields?.Contains(propertyName) == true)
            depth = -1;

        var underlyingType = Nullable.GetUnderlyingType(propType) ?? propType;

        if (underlyingType.IsValueType || propType == typeof(string))
            return value;

        // 循环引用检测：同一个对象实例第二次遇到就不再展开
        if (!propType.IsValueType && !string.IsNullOrEmpty(propertyName))
        {
            if (visited.TryGetValue(value, out _))
                return $"[{value.GetType().Name} (already expanded)]";

            visited.Add(value, value);
        }

        if (propType.GetInterface(nameof(IDictionary)) is not null)
            return depth >= 0
                ? ConvertDict((IDictionary)value, depth - 1, noExpandFields, visited)
                : "[Dictionary]";

        if (propType.GetInterface(nameof(IEnumerable)) is not null)
            return depth >= 0
                ? ToList((IEnumerable)value, depth - 1, noExpandFields, visited: visited)
                : "[List]";

        return depth >= 0
            ? ToDict(value, depth - 1, noExpandFields, visited)
            : value.ToString() ?? "[Null]";
    }

    private static Dictionary<object, object?> ConvertDict(IDictionary dict, int depth,
        HashSet<string>? noExpandFields = null, ConditionalWeakTable<object, object>? visited = null)
    {
        visited ??= new ConditionalWeakTable<object, object>();
        var result = new Dictionary<object, object?>(dict.Count);
        foreach (var key in dict.Keys)
        {
            var val = dict[key];
            result[key] = val is not null
                ? ConvertComplexValue(val.GetType(), val, depth - 1, noExpandFields, visited)
                : null;
        }
        return result;
    }

    /// <summary>
    /// 为 <c>ToList</c> 添加一个接受 <c>visited</c> 参数的重载，供内部递归调用。
    /// </summary>
    private static List<object> ToList(IEnumerable items, int depth, HashSet<string>? noExpandFields,
        ConditionalWeakTable<object, object> visited)
    {
        var result = new List<object>();
        foreach (var item in items)
        {
            if (item is null)
                continue;

            result.Add(ConvertComplexValue(item.GetType(), item, depth, noExpandFields, visited));
        }
        return result;
    }
}
