using System.Text.Json;

namespace LevelUp.NavTableUpdater.Core.Content.PatchHandlers;

internal static class PatchJson
{
    public static string RequiredString(this JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind is not JsonValueKind.String)
        {
            throw new InvalidOperationException($"Patch payload requires string property '{property}'.");
        }

        return value.GetString() ?? throw new InvalidOperationException($"Patch payload property '{property}' is null.");
    }

    public static int RequiredInt32(this JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || !value.TryGetInt32(out var result))
        {
            throw new InvalidOperationException($"Patch payload requires integer property '{property}'.");
        }

        return result;
    }

    public static long RequiredInt64(this JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || !value.TryGetInt64(out var result))
        {
            throw new InvalidOperationException($"Patch payload requires integer property '{property}'.");
        }

        return result;
    }

    public static JsonElement RequiredObject(this JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind is not JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Patch payload requires object property '{property}'.");
        }

        return value;
    }

    public static JsonElement RequiredArray(this JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind is not JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Patch payload requires array property '{property}'.");
        }

        return value;
    }

    public static IReadOnlyList<string> StringArray(this JsonElement element) =>
        element.EnumerateArray().Select(value =>
            value.ValueKind is JsonValueKind.String
                ? value.GetString() ?? ""
                : throw new InvalidOperationException("Patch payload string array contains a non-string value.")).ToArray();
}
