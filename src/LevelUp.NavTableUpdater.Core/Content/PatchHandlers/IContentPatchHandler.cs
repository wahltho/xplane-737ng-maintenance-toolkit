using System.Text.Json;

namespace LevelUp.NavTableUpdater.Core.Content.PatchHandlers;

public interface IContentPatchHandler
{
    string Operation { get; }

    bool SupportsStructuralSourceValidation { get; }

    byte[] Apply(byte[] source, JsonElement payload);
}

public sealed class ContentPatchHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IContentPatchHandler> _handlers;

    public ContentPatchHandlerRegistry(IEnumerable<IContentPatchHandler> handlers)
    {
        _handlers = handlers.ToDictionary(handler => handler.Operation, StringComparer.Ordinal);
    }

    public static ContentPatchHandlerRegistry CreateBuiltIn() =>
        new(
        [
            new ExactTextReplacementsPatchHandler(),
            new Obj8FansLabelsPatchHandler(),
            new SparseBytesPatchHandler(),
            new PngRgbaRegionPatchHandler()
        ]);

    public IContentPatchHandler GetRequired(string operation) =>
        _handlers.TryGetValue(operation, out var handler)
            ? handler
            : throw new InvalidOperationException($"Unsupported declarative patch operation: {operation}.");
}
