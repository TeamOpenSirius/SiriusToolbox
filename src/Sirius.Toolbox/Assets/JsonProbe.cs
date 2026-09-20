using System.Globalization;
using System.Text.Json;

namespace Sirius.Toolbox.Assets;

/// <summary>
/// 主数据 JSON 探测辅助。既支持对象属性名，也支持旧版按位置编号的数组行。
/// Master JSON probing helpers. Both named properties and legacy positional array rows are supported.
/// </summary>
internal static class JsonProbe
{
    public static bool TryGetPropertyOrArrayElement(
        JsonElement row,
        string propertyName,
        int arrayIndex,
        out JsonElement value)
    {
        value = default;
        if (row.ValueKind == JsonValueKind.Object)
            return row.TryGetProperty(propertyName, out value);

        if (row.ValueKind != JsonValueKind.Array
            || arrayIndex < 0
            || row.GetArrayLength() <= arrayIndex)
        {
            return false;
        }

        value = row[arrayIndex];
        return true;
    }

    public static bool TryReadInt32(JsonElement value, out int result)
    {
        if (value.ValueKind == JsonValueKind.Number)
            return value.TryGetInt32(out result);
        if (value.ValueKind == JsonValueKind.String)
            return int.TryParse(value.GetString(), CultureInfo.InvariantCulture, out result);
        result = 0;
        return false;
    }

    public static bool TryReadTimestamp(JsonElement value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (value.ValueKind == JsonValueKind.String)
        {
            return DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out timestamp);
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unixMilliseconds))
        {
            try
            {
                timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        return false;
    }

    public static bool IsAvailableAt(
        JsonElement row,
        DateTimeOffset availableAt,
        string? startDateProperty,
        int startDateIndex,
        string? endDateProperty,
        int endDateIndex)
    {
        if (startDateProperty is not null
            && TryGetPropertyOrArrayElement(row, startDateProperty, startDateIndex, out var startNode)
            && TryReadTimestamp(startNode, out var start)
            && start > availableAt)
        {
            return false;
        }

        if (endDateProperty is not null
            && TryGetPropertyOrArrayElement(row, endDateProperty, endDateIndex, out var endNode)
            && TryReadTimestamp(endNode, out var end)
            && end <= availableAt)
        {
            return false;
        }

        return true;
    }
}
