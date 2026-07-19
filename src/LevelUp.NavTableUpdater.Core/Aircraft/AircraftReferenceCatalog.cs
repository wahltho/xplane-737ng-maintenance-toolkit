using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.Core.Aircraft;

public static class AircraftReferenceCatalog
{
    private const string ZiboFamily = "zibo-737ng";
    private const string Zibo2K = "zibo-737-800-2k";
    private const string Zibo4K = "zibo-737-800-4k";

    private static readonly IReadOnlyList<AircraftReference> References =
    [
        new(
            AircraftId: "zibo-737-800-2k",
            DisplayName: "Zibo 737-800 2K",
            Family: "zibo-737ng",
            AcfFileName: "b738.acf",
            PrefsFileName: "b738_prefs.txt",
            Source: "Zibo Mod Original",
            SourceRef: "B738X_XP12_4_05_35",
            SourceVersion: "4.05.35",
            ExpectedName: "Boeing 737-800X",
            ExpectedDescription: "Boeing 737-800X",
            ExpectedStudioContains: "Zibo",
            ExpectedVersionTxt: "4.05.35",
            ExpectedAcfVersion: "ver 4.05",
            ExpectedFileWriterVersion: "124201",
            ReferenceCgYFeet: -2.000000000,
            ReferenceCgZFeet: 60.340000153),
        new(
            AircraftId: "zibo-737-800-4k",
            DisplayName: "Zibo 737-800 4K",
            Family: "zibo-737ng",
            AcfFileName: "b738_4k.acf",
            PrefsFileName: "b738_4k_prefs.txt",
            Source: "Zibo Mod Original",
            SourceRef: "B738X_XP12_4_05_35",
            SourceVersion: "4.05.35",
            ExpectedName: "Boeing 737-800X (4k)",
            ExpectedDescription: "Boeing 737-800X",
            ExpectedStudioContains: "Zibo",
            ExpectedVersionTxt: "4.05.35",
            ExpectedAcfVersion: "ver 4.05",
            ExpectedFileWriterVersion: "124201",
            ReferenceCgYFeet: -2.000000000,
            ReferenceCgZFeet: 60.340000153),
        new(
            AircraftId: "levelup-737-600",
            DisplayName: "LevelUp 737-600",
            Family: "levelup-737ng",
            AcfFileName: "737_60NG.acf",
            PrefsFileName: "737_60NG_prefs.txt",
            Source: "petrolpram/737NG-Series",
            SourceRef: "origin/main c2ef1e1a",
            SourceVersion: "XP12 V2.S1.50B (20260707-2115 SAO)",
            ExpectedName: "Boeing 737-600NG",
            ExpectedDescription: "Boeing 737-600NG",
            ExpectedStudioContains: "LevelUp",
            ExpectedVersionTxt: null,
            ExpectedAcfVersion: "XP12 V2.S1.50B (20260707-2115 SAO)",
            ExpectedFileWriterVersion: "124311",
            ReferenceCgYFeet: -2.049999952,
            ReferenceCgZFeet: 46.040000916),
        new(
            AircraftId: "levelup-737-700",
            DisplayName: "LevelUp 737-700",
            Family: "levelup-737ng",
            AcfFileName: "737_70NG.acf",
            PrefsFileName: "737_70NG_prefs.txt",
            Source: "petrolpram/737NG-Series",
            SourceRef: "origin/main c2ef1e1a",
            SourceVersion: "XP12 2.S1.50B (20260709-2031 SAO)",
            ExpectedName: "Boeing 737-700NG",
            ExpectedDescription: "Boeing 737-700NG",
            ExpectedStudioContains: "LevelUp",
            ExpectedVersionTxt: null,
            ExpectedAcfVersion: "XP12 2.S1.50B (20260709-2031 SAO)",
            ExpectedFileWriterVersion: "124311",
            ReferenceCgYFeet: -2.049999952,
            ReferenceCgZFeet: 49.740001678),
        new(
            AircraftId: "levelup-737-800",
            DisplayName: "LevelUp 737-800",
            Family: "levelup-737ng",
            AcfFileName: "737_80NG.acf",
            PrefsFileName: "737_80NG_prefs.txt",
            Source: "petrolpram/737NG-Series",
            SourceRef: "origin/main c2ef1e1a",
            SourceVersion: "XP12 FM V2.S1.50B (20260712-1919 SAO)",
            ExpectedName: "Boeing 737-800NG",
            ExpectedDescription: "Boeing 737-800NG",
            ExpectedStudioContains: "LevelUp",
            ExpectedVersionTxt: null,
            ExpectedAcfVersion: "XP12 FM V2.S1.50B (20260712-1919 SAO)",
            ExpectedFileWriterVersion: "124311",
            ReferenceCgYFeet: -2.049999952,
            ReferenceCgZFeet: 60.220001221),
        new(
            AircraftId: "levelup-737-900",
            DisplayName: "LevelUp 737-900",
            Family: "levelup-737ng",
            AcfFileName: "737_90NG.acf",
            PrefsFileName: "737_90NG_prefs.txt",
            Source: "petrolpram/737NG-Series",
            SourceRef: "origin/main c2ef1e1a",
            SourceVersion: "XP12 V2.S1.50B (20260711-2137 SAO)",
            ExpectedName: "Boeing 737-900NG",
            ExpectedDescription: "Boeing 737-900NG",
            ExpectedStudioContains: "LevelUp",
            ExpectedVersionTxt: null,
            ExpectedAcfVersion: "XP12 V2.S1.50B (20260711-2137 SAO)",
            ExpectedFileWriterVersion: "124311",
            ReferenceCgYFeet: -2.049999952,
            ReferenceCgZFeet: 65.800003052),
        new(
            AircraftId: "levelup-737-900er",
            DisplayName: "LevelUp 737-900ER",
            Family: "levelup-737ng",
            AcfFileName: "737_9ENG.acf",
            PrefsFileName: "737_9ENG_prefs.txt",
            Source: "petrolpram/737NG-Series",
            SourceRef: "origin/main c2ef1e1a",
            SourceVersion: "XP12 FM V2.S1.50B (20260712-1921 SAO)",
            ExpectedName: "Boeing 737-900ER",
            ExpectedDescription: "Boeing 737-900ER",
            ExpectedStudioContains: "LevelUp",
            ExpectedVersionTxt: null,
            ExpectedAcfVersion: "XP12 FM V2.S1.50B (20260712-1921 SAO)",
            ExpectedFileWriterVersion: "124311",
            ReferenceCgYFeet: -2.049999952,
            ReferenceCgZFeet: 65.800003052)
    ];

    private static readonly IReadOnlyList<AircraftReferenceCgRange> ReferenceCgRanges =
    [
        ZiboRange(Zibo2K, 0, 2, -1.000000000, 59.500000000),
        ZiboRange(Zibo4K, 0, 2, -1.000000000, 59.500000000),
        ZiboRange(Zibo2K, 3, 4, -1.000000000, 62.419998169),
        ZiboRange(Zibo4K, 3, 4, -1.000000000, 62.419998169),
        ZiboRange(Zibo2K, 5, 14, -1.000000000, 61.419998169),
        ZiboRange(Zibo4K, 5, 14, -1.000000000, 61.419998169),
        ZiboRange(Zibo2K, 15, 15, -1.000000000, 60.720001221),
        ZiboRange(Zibo4K, 15, 15, -1.000000000, 60.720001221),
        ZiboRange(Zibo2K, 16, 17, -2.000000000, 59.979999542),
        ZiboRange(Zibo4K, 16, 17, -2.000000000, 59.979999542),
        ZiboRange(Zibo2K, 18, 35, -2.000000000, 60.340000153),
        ZiboRange(Zibo4K, 18, 35, -2.000000000, 60.340000153),
    ];

    public static IReadOnlyList<AircraftReference> All => References;

    public static IReadOnlyList<AircraftReferenceCgRange> CgRanges => ReferenceCgRanges;

    public static AircraftReference ResolveForKnownCg(
        AircraftReference reference,
        string? localVersion,
        AircraftMaintenanceMetadata? maintenanceMetadata)
    {
        if (!string.Equals(reference.Family, ZiboFamily, StringComparison.OrdinalIgnoreCase))
        {
            return reference;
        }

        var referenceVersionText = !string.IsNullOrWhiteSpace(maintenanceMetadata?.UpstreamBaseVersion)
            ? maintenanceMetadata.UpstreamBaseVersion
            : localVersion;
        if (!AircraftUpstreamVersion.TryParse(referenceVersionText, out var referenceVersion))
        {
            return reference;
        }

        if (string.Equals(reference.SourceVersion, referenceVersion.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return reference;
        }

        var range = ReferenceCgRanges.FirstOrDefault(candidate =>
            string.Equals(candidate.AircraftId, reference.AircraftId, StringComparison.OrdinalIgnoreCase)
            && candidate.Contains(referenceVersion));
        if (range is null)
        {
            return reference;
        }

        var expectedName = reference.ExpectedName;
        var expectedDescription = reference.ExpectedDescription;
        if (string.Equals(reference.AircraftId, Zibo4K, StringComparison.OrdinalIgnoreCase)
            && referenceVersion == new AircraftUpstreamVersion(4, 5, 0))
        {
            expectedName = "Boeing 737-800X";
            expectedDescription = "Boeing 737-800X (4k)";
        }

        return reference with
        {
            SourceRef = range.SourceRef,
            SourceVersion = range.SourceVersion,
            ExpectedName = expectedName,
            ExpectedDescription = expectedDescription,
            ExpectedVersionTxt = maintenanceMetadata is null ? referenceVersion.ToString() : reference.ExpectedVersionTxt,
            ExpectedAcfVersion = null,
            ExpectedFileWriterVersion = null,
            ReferenceCgYFeet = range.ReferenceCgYFeet,
            ReferenceCgZFeet = range.ReferenceCgZFeet
        };
    }

    private static AircraftReferenceCgRange ZiboRange(
        string aircraftId,
        int fromPatch,
        int toPatch,
        double cgYFeet,
        double cgZFeet) =>
        new(
            aircraftId,
            new AircraftUpstreamVersion(4, 5, fromPatch),
            new AircraftUpstreamVersion(4, 5, toPatch),
            "Zibo XP12 ACF CG catalog from Google Drive XP12 packages and Skymatix 4.05.16 torrent",
            cgYFeet,
            cgZFeet);
}
