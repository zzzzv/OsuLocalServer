using System.Collections;
using System.Reflection;
using Realms;

internal static class RealmConverter
{
    public static List<object> ToList(IEnumerable items, int depth)
    {
        var result = new List<object>();
        foreach (var item in items)
            if (item is not null)
                result.Add(ConvertComplexValue(item.GetType(), item, depth));
        return result;
    }

    public static Dictionary<string, object?> ToDict(object obj, int depth)
    {
        var dict = new Dictionary<string, object?>();
        var props = GetMappedProperties(obj.GetType());

        foreach (var prop in props)
        {
            var value = prop.GetValue(obj);
            if (value is null)
                continue;

            dict[prop.Name] = ConvertComplexValue(prop.PropertyType, value, depth);
        }

        return dict;
    }

    private static PropertyInfo[] GetMappedProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == type)
            .ToArray();
    }

    private static object ConvertComplexValue(Type propType, object value, int depth)
    {
        var underlyingType = Nullable.GetUnderlyingType(propType) ?? propType;

        if (underlyingType.IsValueType || propType == typeof(string))
            return value;

        if (propType.GetInterface(nameof(IDictionary)) is not null)
            return depth >= 0 ? ConvertDict((IDictionary)value, depth - 1) : "[Dictionary]";

        if (propType.GetInterface(nameof(IEnumerable)) is not null)
            return depth >= 0 ? ToList((IEnumerable)value, depth - 1) : "[List]";

        return depth >= 0 ? ToDict(value, depth - 1) : value.ToString() ?? "[Null]";
    }

    private static Dictionary<object, object?> ConvertDict(IDictionary dict, int depth)
    {
        var result = new Dictionary<object, object?>(dict.Count);
        foreach (var key in dict.Keys)
        {
            var val = dict[key];
            result[key] = val is not null
                ? ConvertComplexValue(val.GetType(), val, depth - 1)
                : null;
        }
        return result;
    }
}
