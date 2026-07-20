using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.Core.Aircraft;

public static class AircraftReferenceCatalog
{
    private const string ZiboFamily = "zibo-737ng";
    private const string Zibo2K = "zibo-737-800-2k";
    private const string Zibo4K = "zibo-737-800-4k";
    private const string LevelUpFamily = "levelup-737ng";
    private const string LevelUp600 = "levelup-737-600";
    private const string LevelUp700 = "levelup-737-700";
    private const string LevelUp800 = "levelup-737-800";
    private const string LevelUp900 = "levelup-737-900";
    private const string LevelUp900Er = "levelup-737-900er";

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
        LevelUpRange(
            LevelUp600,
            "petrolpram/737NG-Series through v2.S1.01",
            "XP12 FM 2.0.3",
            -2.049999952,
            46.139999390,
            sourceRefs: ["8c67fd1", "7c66ff6f1f15bee3c3735d108fce4202ca91cfcf", "96e58d1fdc63e8be5015c76daa9237a1e8f17df1"],
            releaseTags: ["v2-alpha10", "v2.S1.01"],
            acfVersions: ["XP12 FM 2.0.3"]),
        LevelUpRange(
            LevelUp600,
            "petrolpram/737NG-Series f76e128..c2ef1e1a",
            "XP12 V2.S1.50B (20260707-2115 SAO)",
            -2.049999952,
            46.040000916,
            sourceRefs: ["f76e128", "a3c558edd191203e251ccfc4fe4c151e97651ec4", "5ab6c3ae3096428abc3503211e13f6b9076c7fa7", "c2ef1e1a91896aadee8a415fcdefbb7933f013b8"],
            releaseTags: ["v2.S1.50B", "v2.S1.50C"],
            acfVersions: ["XP12 V2.S1.50B (20260707-2115 SAO)"]),
        LevelUpRange(
            LevelUp700,
            "petrolpram/737NG-Series through v2.S1.01",
            "XP12 FM 2.0.3",
            -2.049999952,
            50.840000153,
            sourceRefs: ["8c67fd1", "7c66ff6f1f15bee3c3735d108fce4202ca91cfcf", "96e58d1fdc63e8be5015c76daa9237a1e8f17df1"],
            releaseTags: ["v2-alpha10", "v2.S1.01"],
            acfVersions: ["XP12 FM 2.0.3"]),
        LevelUpRange(
            LevelUp700,
            "petrolpram/737NG-Series f76e128..c2ef1e1a",
            "XP12 2.S1.50B (20260709-2031 SAO)",
            -2.049999952,
            49.740001678,
            sourceRefs: ["f76e128", "a3c558edd191203e251ccfc4fe4c151e97651ec4", "5ab6c3ae3096428abc3503211e13f6b9076c7fa7", "c2ef1e1a91896aadee8a415fcdefbb7933f013b8"],
            releaseTags: ["v2.S1.50B", "v2.S1.50C"],
            acfVersions: ["XP12 2.S1.50a (Jochen Heiden 2 Jul 26)", "XP12 2.S1.50B (20260709-2031 SAO)"]),
        LevelUpRange(
            LevelUp800,
            "petrolpram/737NG-Series v2-alpha10",
            "v2-alpha10",
            -2.049999952,
            60.220001221,
            sourceRefs: ["8c67fd1", "7c66ff6f1f15bee3c3735d108fce4202ca91cfcf"],
            releaseTags: ["v2-alpha10"]),
        LevelUpRange(
            LevelUp800,
            "petrolpram/737NG-Series 36f9fe5",
            "36f9fe5",
            -1.049999952,
            59.500000000,
            sourceRefs: ["36f9fe5"]),
        LevelUpRange(
            LevelUp800,
            "petrolpram/737NG-Series v2.S1.01",
            "v2.S1.01",
            -1.049999952,
            60.299999237,
            sourceRefs: ["2723547", "96e58d1fdc63e8be5015c76daa9237a1e8f17df1"],
            releaseTags: ["v2.S1.01"]),
        LevelUpRange(
            LevelUp800,
            "petrolpram/737NG-Series 4a0bb70",
            "4a0bb70",
            -1.049999952,
            60.220001221,
            sourceRefs: ["4a0bb70"]),
        LevelUpRange(
            LevelUp800,
            "petrolpram/737NG-Series f76e128..c2ef1e1a",
            "XP12 FM V2.S1.50B (20260712-1919 SAO)",
            -2.049999952,
            60.220001221,
            sourceRefs: ["f76e128", "a3c558edd191203e251ccfc4fe4c151e97651ec4", "5ab6c3ae3096428abc3503211e13f6b9076c7fa7", "c2ef1e1a91896aadee8a415fcdefbb7933f013b8"],
            releaseTags: ["v2.S1.50B", "v2.S1.50C"],
            acfVersions: ["XP12 FM V2.S1.50B (20260712-1919 SAO)"]),
        LevelUpRange(
            LevelUp900,
            "petrolpram/737NG-Series through v2.S1.01",
            "XP12 FM 2.0.3",
            -2.049999952,
            66.339996338,
            sourceRefs: ["8c67fd1", "7c66ff6f1f15bee3c3735d108fce4202ca91cfcf", "96e58d1fdc63e8be5015c76daa9237a1e8f17df1"],
            releaseTags: ["v2-alpha10", "v2.S1.01"],
            acfVersions: ["XP12 FM 2.0.3"]),
        LevelUpRange(
            LevelUp900,
            "petrolpram/737NG-Series v2.S1.50B",
            "v2.S1.50B",
            -2.049999952,
            65.650001526,
            sourceRefs: ["f76e128", "a3c558edd191203e251ccfc4fe4c151e97651ec4"],
            releaseTags: ["v2.S1.50B"]),
        LevelUpRange(
            LevelUp900,
            "petrolpram/737NG-Series ff32156..c2ef1e1a",
            "XP12 V2.S1.50B (20260711-2137 SAO)",
            -2.049999952,
            65.800003052,
            sourceRefs: ["ff32156", "5ab6c3ae3096428abc3503211e13f6b9076c7fa7", "c2ef1e1a91896aadee8a415fcdefbb7933f013b8"],
            releaseTags: ["v2.S1.50C"],
            acfVersions: ["XP12 V2.S1.50B (20260711-2137 SAO)"]),
        LevelUpRange(
            LevelUp900Er,
            "petrolpram/737NG-Series through v2.S1.01",
            "XP12 FM 2.0.3",
            -2.049999952,
            66.339996338,
            sourceRefs: ["8c67fd1", "7c66ff6f1f15bee3c3735d108fce4202ca91cfcf", "96e58d1fdc63e8be5015c76daa9237a1e8f17df1"],
            releaseTags: ["v2-alpha10", "v2.S1.01"],
            acfVersions: ["XP12 FM 2.0.3"]),
        LevelUpRange(
            LevelUp900Er,
            "petrolpram/737NG-Series f76e128..c2ef1e1a",
            "XP12 FM V2.S1.50B (20260712-1921 SAO)",
            -2.049999952,
            65.800003052,
            sourceRefs: ["f76e128", "a3c558edd191203e251ccfc4fe4c151e97651ec4", "5ab6c3ae3096428abc3503211e13f6b9076c7fa7", "c2ef1e1a91896aadee8a415fcdefbb7933f013b8"],
            releaseTags: ["v2.S1.50B", "v2.S1.50C"],
            acfVersions: ["XP12 FM V2.S1.50B (20260712-1921 SAO)"]),
    ];

    public static IReadOnlyList<AircraftReference> All => References;

    public static IReadOnlyList<AircraftReferenceCgRange> CgRanges => ReferenceCgRanges;

    public static AircraftReference ResolveForKnownCg(
        AircraftReference reference,
        string? localVersion,
        string? acfVersion,
        AircraftMaintenanceMetadata? maintenanceMetadata)
    {
        if (string.Equals(reference.Family, ZiboFamily, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveZiboReference(reference, localVersion, maintenanceMetadata);
        }

        if (string.Equals(reference.Family, LevelUpFamily, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveLevelUpReference(reference, acfVersion, maintenanceMetadata);
        }

        return reference;
    }

    private static AircraftReference ResolveZiboReference(
        AircraftReference reference,
        string? localVersion,
        AircraftMaintenanceMetadata? maintenanceMetadata)
    {
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

    private static AircraftReference ResolveLevelUpReference(
        AircraftReference reference,
        string? acfVersion,
        AircraftMaintenanceMetadata? maintenanceMetadata)
    {
        if (maintenanceMetadata is null
            && string.Equals(acfVersion?.Trim(), reference.ExpectedAcfVersion?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return reference;
        }

        var candidates = ReferenceCgRanges
            .Where(candidate => string.Equals(candidate.AircraftId, reference.AircraftId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var range = candidates.FirstOrDefault(candidate => candidate.MatchesSourceRef(maintenanceMetadata?.UpstreamSourceRef))
            ?? candidates.FirstOrDefault(candidate => candidate.MatchesSourceRef(maintenanceMetadata?.UpstreamBaseVersion))
            ?? candidates.FirstOrDefault(candidate => candidate.MatchesReleaseTag(maintenanceMetadata?.UpstreamReleaseTag))
            ?? candidates.FirstOrDefault(candidate => candidate.MatchesReleaseTag(maintenanceMetadata?.UpstreamBaseVersion))
            ?? candidates.FirstOrDefault(candidate => candidate.MatchesAcfVersion(acfVersion));

        if (range is null)
        {
            return reference;
        }

        return reference with
        {
            SourceRef = range.SourceRef,
            SourceVersion = range.SourceVersion,
            ExpectedAcfVersion = acfVersion,
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
        double cgZFeet)
    {
        var fromVersion = new AircraftUpstreamVersion(4, 5, fromPatch);
        var toVersion = new AircraftUpstreamVersion(4, 5, toPatch);
        var sourceVersion = fromVersion == toVersion ? fromVersion.ToString() : $"{fromVersion}-{toVersion}";

        return new(
            aircraftId,
            fromVersion,
            toVersion,
            "Zibo XP12 ACF CG catalog from Google Drive XP12 packages and Skymatix 4.05.16 torrent",
            sourceVersion,
            cgYFeet,
            cgZFeet,
            SourceRefs: [],
            ReleaseTags: [],
            AcfVersions: []);
    }

    private static AircraftReferenceCgRange LevelUpRange(
        string aircraftId,
        string sourceRef,
        string sourceVersion,
        double cgYFeet,
        double cgZFeet,
        IReadOnlyList<string>? sourceRefs = null,
        IReadOnlyList<string>? releaseTags = null,
        IReadOnlyList<string>? acfVersions = null) =>
        new(
            aircraftId,
            FromVersion: null,
            ToVersion: null,
            sourceRef,
            sourceVersion,
            cgYFeet,
            cgZFeet,
            sourceRefs ?? [],
            releaseTags ?? [],
            acfVersions ?? []);
}
