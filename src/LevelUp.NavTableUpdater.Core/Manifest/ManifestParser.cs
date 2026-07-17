using System.Globalization;

namespace LevelUp.NavTableUpdater.Core.Manifest;

public static class ManifestParser
{
    public static PackageManifest ParsePipeManifest(string manifestText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestText);

        var schemaVersion = 1;
        var packageId = "";
        var packageVersion = "";
        var releaseTag = "";
        var aircraftFamily = "";
        var repositoryUrl = "";
        var targetRelativePath = "";
        var payloads = new List<PayloadFile>();
        var anchors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var beginMarkers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var endMarkers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var legacySignatures = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in manifestText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('|');
            if (parts.Length < 2)
            {
                continue;
            }

            switch (parts[0])
            {
                case "schema" when parts is ["schema", "package-manifest", var version]:
                    schemaVersion = int.Parse(version, CultureInfo.InvariantCulture);
                    break;
                case "package" when parts is ["package", "id", var value]:
                    packageId = value;
                    break;
                case "package" when parts is ["package", "version", var value]:
                    packageVersion = value;
                    break;
                case "package" when parts is ["package", "release_tag", var value]:
                    releaseTag = value;
                    break;
                case "aircraft" when parts is ["aircraft", "family", var value]:
                    aircraftFamily = value;
                    break;
                case "repository" when parts is ["repository", "url", var value]:
                    repositoryUrl = value;
                    break;
                case "target" when parts is ["target", "relative_path", var value]:
                    targetRelativePath = value;
                    break;
                case "payload":
                    payloads.Add(ParsePayload(parts));
                    break;
                case "anchor" when parts.Length >= 3:
                    anchors[parts[1]] = string.Join('|', parts.Skip(2));
                    break;
                case "marker" when parts.Length >= 4 && parts[2] == "begin":
                    beginMarkers[parts[1]] = string.Join('|', parts.Skip(3));
                    break;
                case "marker" when parts.Length >= 4 && parts[2] == "end":
                    endMarkers[parts[1]] = string.Join('|', parts.Skip(3));
                    break;
                case "legacy" when parts.Length >= 4:
                    var id = parts[2];
                    if (!legacySignatures.TryGetValue(id, out var signatures))
                    {
                        signatures = [];
                        legacySignatures[id] = signatures;
                    }

                    signatures.Add(string.Join('|', parts.Skip(3)));
                    break;
            }
        }

        var patchOperations = anchors.Keys
            .OrderBy(PatchOperationOrder)
            .Select(id => new PatchOperation(
                Id: id,
                Anchor: anchors[id],
                BeginMarker: beginMarkers.GetValueOrDefault(id, ""),
                EndMarker: endMarkers.GetValueOrDefault(id, ""),
                LegacySignatures: legacySignatures.GetValueOrDefault(id, [])))
            .ToArray();

        return new PackageManifest(
            SchemaVersion: schemaVersion,
            PackageId: packageId,
            PackageVersion: packageVersion,
            ReleaseTag: releaseTag,
            ReleaseChannel: "bundled",
            RepositoryUrl: repositoryUrl,
            AircraftFamily: aircraftFamily,
            TargetRelativePath: targetRelativePath,
            Payloads: payloads,
            PatchOperations: patchOperations);
    }

    private static PayloadFile ParsePayload(string[] parts)
    {
        if (parts.Length < 7 || parts[3] != "size" || parts[5] != "sha256")
        {
            throw new FormatException($"Unsupported payload manifest line: {string.Join('|', parts)}");
        }

        return new PayloadFile(
            Id: parts[1],
            FileName: parts[2],
            Size: long.Parse(parts[4], CultureInfo.InvariantCulture),
            Sha256: parts[6]);
    }

    private static int PatchOperationOrder(string id) =>
        id.ToLowerInvariant() switch
        {
            "dofile" => 0,
            "kias" => 1,
            "mach" => 2,
            _ => 10
        };
}
