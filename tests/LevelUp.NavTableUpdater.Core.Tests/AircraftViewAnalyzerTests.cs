using System.Text;
using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class AircraftViewAnalyzerTests
{
    [Fact]
    public void ReferenceCatalog_ContainsZiboAndAllLevelUpBaselines()
    {
        var references = AircraftReferenceCatalog.All;

        Assert.Contains(references, reference => reference.AircraftId == "zibo-737-800-2k"
            && reference.ReferenceCgYFeet == -2.000000000
            && reference.ReferenceCgZFeet == 60.340000153);
        Assert.Contains(references, reference => reference.AircraftId == "zibo-737-800-4k"
            && reference.ReferenceCgYFeet == -2.000000000
            && reference.ReferenceCgZFeet == 60.340000153);
        Assert.Contains(AircraftReferenceCatalog.CgRanges, range => range.AircraftId == "zibo-737-800-2k"
            && range.FromVersion!.ToString() == "4.05.00"
            && range.ToVersion!.ToString() == "4.05.02"
            && range.ReferenceCgYFeet == -1.000000000
            && range.ReferenceCgZFeet == 59.500000000);
        Assert.Contains(AircraftReferenceCatalog.CgRanges, range => range.AircraftId == "zibo-737-800-2k"
            && range.FromVersion!.ToString() == "4.05.18"
            && range.ToVersion!.ToString() == "4.05.35"
            && range.ReferenceCgYFeet == -2.000000000
            && range.ReferenceCgZFeet == 60.340000153);
        Assert.Contains(AircraftReferenceCatalog.CgRanges, range => range.AircraftId == "levelup-737-700"
            && range.MatchesReleaseTag("v2.S1.01")
            && range.ReferenceCgYFeet == -2.049999952
            && range.ReferenceCgZFeet == 50.840000153);
        Assert.Contains(AircraftReferenceCatalog.CgRanges, range => range.AircraftId == "levelup-737-800"
            && range.MatchesSourceRef("petrolpram/737NG-Series@2723547")
            && range.ReferenceCgYFeet == -1.049999952
            && range.ReferenceCgZFeet == 60.299999237);
        Assert.Contains(AircraftReferenceCatalog.CgRanges, range => range.AircraftId == "levelup-737-900"
            && range.MatchesReleaseTag("v2.S1.50B")
            && range.ReferenceCgYFeet == -2.049999952
            && range.ReferenceCgZFeet == 65.650001526);
        Assert.Contains(references, reference => reference.AircraftId == "levelup-737-600"
            && reference.ReferenceCgZFeet == 46.040000916);
        Assert.Contains(references, reference => reference.AircraftId == "levelup-737-700"
            && reference.ReferenceCgZFeet == 49.740001678);
        Assert.Contains(references, reference => reference.AircraftId == "levelup-737-800"
            && reference.ReferenceCgZFeet == 60.220001221);
        Assert.Contains(references, reference => reference.AircraftId == "levelup-737-900"
            && reference.ReferenceCgZFeet == 65.800003052);
        Assert.Contains(references, reference => reference.AircraftId == "levelup-737-900er"
            && reference.ReferenceCgZFeet == 65.800003052);
    }

    [Fact]
    public void Analyze_WhenReferenceCgAndQv0Match_ReturnsReferenceCgAndDefaultViewMatch()
    {
        using var fixture = AircraftViewFixture.CreateLevelUp700(
            cgY: -2.049999952,
            cgZ: 49.740001678,
            defaultViewMatchesQv0: true);

        var result = new AircraftViewAnalyzer().Analyze(fixture.Path);
        var variant = Assert.Single(result.Variants);

        Assert.Equal("Reference CG", result.StateLabel);
        Assert.Equal("levelup-737-700", variant.AircraftId);
        Assert.Equal("Reference CG", variant.Status);
        Assert.Equal("Expected metadata", variant.IdentityStatus);
        Assert.Equal("QV0 readable", variant.QuickViewStatus);
        Assert.Equal("Default view matches QV0", variant.DefaultViewStatus);
        Assert.InRange(variant.DeltaZFeet!.Value, -0.000001, 0.000001);
    }

    [Fact]
    public void Analyze_WhenLaminarStockB738AcfExists_DoesNotReturnZiboProduct()
    {
        using var fixture = AircraftViewFixture.CreateLaminarStock737();

        var result = new AircraftViewAnalyzer().Analyze(fixture.Path);

        Assert.Empty(result.Variants);
        Assert.Equal("No supported 737NG variant", result.StateLabel);
        Assert.Contains(result.Findings, finding => finding.Contains("does not match the expected Zibo or LevelUp product identity", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_WhenAircraftRootContainsSupportedProducts_ReturnsZiboAndLevelUpOnly()
    {
        using var zibo = AircraftViewFixture.CreateZibo2K();
        using var levelUp = AircraftViewFixture.CreateLevelUp700(
            cgY: -2.049999952,
            cgZ: 49.740001678,
            defaultViewMatchesQv0: true);
        using var laminar = AircraftViewFixture.CreateLaminarStock737();
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xplane-737ng-root-tests-{Guid.NewGuid():N}");
        var aircraftRoot = System.IO.Path.Combine(root, "Aircraft");
        var laminarRoot = System.IO.Path.Combine(aircraftRoot, "Laminar Research");
        Directory.CreateDirectory(aircraftRoot);
        Directory.CreateDirectory(laminarRoot);

        Directory.Move(zibo.Path, System.IO.Path.Combine(aircraftRoot, "B737-800X"));
        Directory.Move(levelUp.Path, System.IO.Path.Combine(aircraftRoot, "737NG Series"));
        Directory.Move(laminar.Path, System.IO.Path.Combine(laminarRoot, "Boeing 737-800"));

        try
        {
            var result = new AircraftViewAnalyzer().Analyze(aircraftRoot);

            Assert.Equal(2, result.Variants.Count);
            Assert.Contains(result.Variants, variant => variant.Family == "zibo-737ng");
            Assert.Contains(result.Variants, variant => variant.Family == "levelup-737ng");
            Assert.DoesNotContain(result.Variants, variant => variant.DisplayName.Contains("Laminar", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Analyze_WhenCustomAcfCgDiffers_ReturnsCgDeltaWithoutHashGating()
    {
        using var fixture = AircraftViewFixture.CreateLevelUp700(
            cgY: -2.049999952,
            cgZ: 49.840001678,
            defaultViewMatchesQv0: true);

        var result = new AircraftViewAnalyzer().Analyze(fixture.Path);
        var variant = Assert.Single(result.Variants);

        Assert.Equal("CG delta detected", result.StateLabel);
        Assert.Equal("CG delta detected", variant.Status);
        Assert.Equal(0.100000000, variant.DeltaZFeet!.Value, precision: 6);
        Assert.Equal(0.030480000, variant.DeltaZMeters!.Value, precision: 6);
        Assert.Contains(result.Findings, finding => finding.Contains("CG differs from reference baseline", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_WhenLevelUpHistoricalAcfVersionIsKnown_UsesHistoricalReferenceCg()
    {
        using var fixture = AircraftViewFixture.CreateLevelUp700(
            cgY: -2.049999952,
            cgZ: 50.840000153,
            defaultViewMatchesQv0: true,
            acfVersion: "XP12 FM 2.0.3",
            writer: "124004");

        var result = new AircraftViewAnalyzer().Analyze(fixture.Path);
        var variant = Assert.Single(result.Variants);

        Assert.Equal("Reference CG", result.StateLabel);
        Assert.Equal("XP12 FM 2.0.3", variant.SourceVersion);
        Assert.Equal(50.840000153, variant.ReferenceCgZFeet);
        Assert.Equal(0, variant.DeltaZFeet!.Value, precision: 6);
        Assert.Equal("Expected metadata", variant.IdentityStatus);
    }

    [Fact]
    public void Analyze_WhenLevelUpHistoricalReleaseTagMetadataIsPresent_UsesTaggedReferenceCg()
    {
        using var fixture = AircraftViewFixture.CreateLevelUp900(
            cgY: -2.049999952,
            cgZ: 65.650001526,
            defaultViewMatchesQv0: true,
            acfVersion: "XP12 custom no-lua port",
            writer: "124311");
        File.WriteAllText(
            System.IO.Path.Combine(fixture.Path, AircraftMaintenanceMetadata.FileName),
            """
            {
              "schemaVersion": 1,
              "aircraftFamily": "levelup-737ng",
              "variant": "levelup-737-900",
              "distribution": "wahltho-no-lua-port",
              "distributionVersion": "5.00.00",
              "upstreamFamily": "levelup-737ng",
              "upstreamReleaseTag": "v2.S1.50B",
              "runtime": "no-lua-cpp"
            }
            """,
            new UTF8Encoding(false));

        var result = new AircraftViewAnalyzer().Analyze(fixture.Path);
        var variant = Assert.Single(result.Variants);

        Assert.Equal("Reference CG", result.StateLabel);
        Assert.Equal("5.00.00", variant.LocalVersion);
        Assert.Equal("v2.S1.50B", variant.SourceVersion);
        Assert.Equal(65.650001526, variant.ReferenceCgZFeet);
        Assert.Equal(0, variant.DeltaZFeet!.Value, precision: 6);
        Assert.Equal("Custom distribution (wahltho-no-lua-port 5.00.00)", variant.IdentityStatus);
    }

    [Fact]
    public void Analyze_WhenLevelUpHistoricalSourceRefMetadataIsPresent_UsesCommitReferenceCg()
    {
        using var fixture = AircraftViewFixture.CreateLevelUp800(
            cgY: -1.049999952,
            cgZ: 60.299999237,
            defaultViewMatchesQv0: true,
            acfVersion: "XP12 FM 2.0.3",
            writer: "124004");
        File.WriteAllText(
            System.IO.Path.Combine(fixture.Path, AircraftMaintenanceMetadata.FileName),
            """
            {
              "schemaVersion": 1,
              "aircraftFamily": "levelup-737ng",
              "variant": "levelup-737-800",
              "distribution": "wahltho-no-lua-port",
              "distributionVersion": "5.00.00",
              "upstreamFamily": "levelup-737ng",
              "upstreamSourceRef": "petrolpram/737NG-Series@2723547",
              "runtime": "no-lua-cpp"
            }
            """,
            new UTF8Encoding(false));

        var result = new AircraftViewAnalyzer().Analyze(fixture.Path);
        var variant = Assert.Single(result.Variants);

        Assert.Equal("Reference CG", result.StateLabel);
        Assert.Equal("v2.S1.01", variant.SourceVersion);
        Assert.Equal(-1.049999952, variant.ReferenceCgYFeet);
        Assert.Equal(60.299999237, variant.ReferenceCgZFeet);
        Assert.Equal(0, variant.DeltaYFeet!.Value, precision: 6);
        Assert.Equal(0, variant.DeltaZFeet!.Value, precision: 6);
    }

    [Fact]
    public void Analyze_WhenLevelUp800HistoricalAcfVersionIsAmbiguousWithoutMetadata_DoesNotGuess()
    {
        using var fixture = AircraftViewFixture.CreateLevelUp800(
            cgY: -1.049999952,
            cgZ: 60.299999237,
            defaultViewMatchesQv0: true,
            acfVersion: "XP12 FM 2.0.3",
            writer: "124004");

        var result = new AircraftViewAnalyzer().Analyze(fixture.Path);
        var variant = Assert.Single(result.Variants);

        Assert.Equal("CG delta detected", result.StateLabel);
        Assert.Equal("XP12 FM V2.S1.50B (20260712-1919 SAO)", variant.SourceVersion);
        Assert.Equal(1.000000000, variant.DeltaYFeet!.Value, precision: 6);
        Assert.Equal(0.079998016, variant.DeltaZFeet!.Value, precision: 6);
    }

    [Fact]
    public void Analyze_WhenZiboVersionTxtIsPresent_UsesZiboReferenceMetadata()
    {
        using var fixture = AircraftViewFixture.CreateZibo2K();

        var result = new AircraftViewAnalyzer().Analyze(fixture.Path);
        var variant = Assert.Single(result.Variants);

        Assert.Equal("zibo-737-800-2k", variant.AircraftId);
        Assert.Equal("4.05.35", variant.LocalVersion);
        Assert.Equal("ver 4.05", variant.AcfVersion);
        Assert.Equal("Expected metadata", variant.IdentityStatus);
        Assert.Equal("Reference CG", variant.Status);
    }

    [Fact]
    public void Analyze_WhenZiboVersionTxtMatchesHistoricalCgRange_UsesHistoricalReferenceCg()
    {
        using var fixture = AircraftViewFixture.CreateZibo2K(
            localVersion: "4.05.03",
            cgY: -1.000000000,
            cgZ: 62.419998169,
            acfVersion: "Early access",
            writer: "123008");

        var result = new AircraftViewAnalyzer().Analyze(fixture.Path);
        var variant = Assert.Single(result.Variants);

        Assert.Equal("Reference CG", result.StateLabel);
        Assert.Equal("4.05.03-4.05.04", variant.SourceVersion);
        Assert.Equal(-1.000000000, variant.ReferenceCgYFeet);
        Assert.Equal(62.419998169, variant.ReferenceCgZFeet);
        Assert.Equal(0, variant.DeltaYFeet!.Value, precision: 6);
        Assert.Equal(0, variant.DeltaZFeet!.Value, precision: 6);
        Assert.Equal("Expected metadata", variant.IdentityStatus);
        Assert.Contains(result.Findings, finding => finding.Contains("reference CG selected from 4.05.03-4.05.04", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_WhenZiboHistoricalVersionHasUnexpectedCg_ReportsDeltaFromHistoricalReference()
    {
        using var fixture = AircraftViewFixture.CreateZibo2K(
            localVersion: "4.05.15",
            cgY: -1.000000000,
            cgZ: 60.340000153,
            acfVersion: "ver 4.05",
            writer: "124001");

        var result = new AircraftViewAnalyzer().Analyze(fixture.Path);
        var variant = Assert.Single(result.Variants);

        Assert.Equal("CG delta detected", result.StateLabel);
        Assert.Equal("4.05.15", variant.SourceVersion);
        Assert.Equal(60.720001221, variant.ReferenceCgZFeet);
        Assert.Equal(-0.380001068, variant.DeltaZFeet!.Value, precision: 6);
    }

    [Fact]
    public void Analyze_WhenMaintenanceMetadataIsPresent_UsesDistributionVersionBeforeVersionTxt()
    {
        using var fixture = AircraftViewFixture.CreateZibo2K();
        File.WriteAllText(
            System.IO.Path.Combine(fixture.Path, "xplane-737ng-maintenance.json"),
            """
            {
              "schemaVersion": 1,
              "aircraftFamily": "zibo-737ng",
              "variant": "zibo-737-800-2k",
              "distribution": "wahltho-no-lua-port",
              "distributionVersion": "5.00.00",
              "upstreamFamily": "zibo-737ng",
              "upstreamBaseVersion": "4.05.35",
              "runtime": "no-lua-cpp"
            }
            """,
            new UTF8Encoding(false));

        var result = new AircraftViewAnalyzer().Analyze(fixture.Path);
        var variant = Assert.Single(result.Variants);

        Assert.Equal("5.00.00", variant.LocalVersion);
        Assert.Equal("Custom distribution (wahltho-no-lua-port 5.00.00)", variant.IdentityStatus);
        Assert.Contains(result.Findings, finding => finding.Contains("xplane-737ng-maintenance.json", StringComparison.Ordinal));
        Assert.Contains(result.Findings, finding => finding.Contains("wahltho-no-lua-port", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_WhenMaintenanceMetadataHasUpstreamBaseVersion_UsesUpstreamCgRangeForCustomPort()
    {
        using var fixture = AircraftViewFixture.CreateZibo2K(
            localVersion: "5.00.00",
            cgY: -1.000000000,
            cgZ: 60.720001221,
            acfVersion: "ver 4.05",
            writer: "124001");
        File.WriteAllText(
            System.IO.Path.Combine(fixture.Path, "xplane-737ng-maintenance.json"),
            """
            {
              "schemaVersion": 1,
              "aircraftFamily": "zibo-737ng",
              "variant": "zibo-737-800-2k",
              "distribution": "wahltho-no-lua-port",
              "distributionVersion": "5.00.00",
              "upstreamFamily": "zibo-737ng",
              "upstreamBaseVersion": "4.05.15",
              "runtime": "no-lua-cpp"
            }
            """,
            new UTF8Encoding(false));

        var result = new AircraftViewAnalyzer().Analyze(fixture.Path);
        var variant = Assert.Single(result.Variants);

        Assert.Equal("5.00.00", variant.LocalVersion);
        Assert.Equal("4.05.15", variant.SourceVersion);
        Assert.Equal("Reference CG", variant.Status);
        Assert.Equal(60.720001221, variant.ReferenceCgZFeet);
        Assert.Equal(0, variant.DeltaZFeet!.Value, precision: 6);
    }

    private sealed class AircraftViewFixture : IDisposable
    {
        private AircraftViewFixture(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static AircraftViewFixture CreateLevelUp700(
            double cgY,
            double cgZ,
            bool defaultViewMatchesQv0,
            string acfVersion = "XP12 2.S1.50B (20260709-2031 SAO)",
            string writer = "124311") =>
            CreateLevelUpVariant(
                acfFileName: "737_70NG.acf",
                prefsFileName: "737_70NG_prefs.txt",
                name: "Boeing 737-700NG",
                description: "Boeing 737-700NG",
                acfVersion,
                writer,
                cgY,
                cgZ,
                defaultViewMatchesQv0);

        public static AircraftViewFixture CreateLevelUp800(
            double cgY,
            double cgZ,
            bool defaultViewMatchesQv0,
            string acfVersion = "XP12 FM V2.S1.50B (20260712-1919 SAO)",
            string writer = "124311") =>
            CreateLevelUpVariant(
                acfFileName: "737_80NG.acf",
                prefsFileName: "737_80NG_prefs.txt",
                name: "Boeing 737-800NG",
                description: "Boeing 737-800NG",
                acfVersion,
                writer,
                cgY,
                cgZ,
                defaultViewMatchesQv0);

        public static AircraftViewFixture CreateLevelUp900(
            double cgY,
            double cgZ,
            bool defaultViewMatchesQv0,
            string acfVersion = "XP12 V2.S1.50B (20260711-2137 SAO)",
            string writer = "124311") =>
            CreateLevelUpVariant(
                acfFileName: "737_90NG.acf",
                prefsFileName: "737_90NG_prefs.txt",
                name: "Boeing 737-900NG",
                description: "Boeing 737-900NG",
                acfVersion,
                writer,
                cgY,
                cgZ,
                defaultViewMatchesQv0);

        private static AircraftViewFixture CreateLevelUpVariant(
            string acfFileName,
            string prefsFileName,
            string name,
            string description,
            string acfVersion,
            string writer,
            double cgY,
            double cgZ,
            bool defaultViewMatchesQv0)
        {
            var root = CreateRoot();
            var qv0 = new QuickView0(XMeters: 1.0, YMeters: 2.0, ZMeters: -3.0, PitchDegrees: -2.5);
            var defaultView = defaultViewMatchesQv0
                ? AircraftFileParser.CalculateDefaultViewFromQuickView(new AircraftCg(cgY, cgZ), qv0)
                : new DefaultView(0.0, 0.0, 0.0, 0.0);

            WriteText(
                System.IO.Path.Combine(root, acfFileName),
                BuildAcf(
                    name: name,
                    description: description,
                    studio: "LevelUp, Laminar Research, ZiboMod, flight tuned by Aeroguitarist",
                    version: acfVersion,
                    writer: writer,
                    cgY,
                    cgZ,
                    defaultView));
            WritePrefs(System.IO.Path.Combine(root, prefsFileName), qv0);
            return new AircraftViewFixture(root);
        }

        public static AircraftViewFixture CreateZibo2K(
            string localVersion = "4.05.35",
            double cgY = -2.000000000,
            double cgZ = 60.340000153,
            string acfVersion = "ver 4.05",
            string writer = "124201")
        {
            var root = CreateRoot();
            WriteText(System.IO.Path.Combine(root, "version.txt"), $"{localVersion}\n");
            WriteText(
                System.IO.Path.Combine(root, "b738.acf"),
                BuildAcf(
                    name: "Boeing 737-800X",
                    description: "Boeing 737-800X",
                    studio: "Laminar Research modified by Zibo and Twkster",
                    version: acfVersion,
                    writer: writer,
                    cgY: cgY,
                    cgZ: cgZ,
                    defaultView: new DefaultView(0.0, 0.0, 0.0, 0.0)));
            return new AircraftViewFixture(root);
        }

        public static AircraftViewFixture CreateLaminarStock737()
        {
            var root = CreateRoot();
            WriteText(
                System.IO.Path.Combine(root, "b738.acf"),
                BuildAcf(
                    name: "Boeing 737-800",
                    description: "Boeing 737-800",
                    studio: "Laminar Research",
                    version: "XP12",
                    writer: "124311",
                    cgY: -1.0,
                    cgZ: 60.0,
                    defaultView: new DefaultView(0.0, 0.0, 0.0, 0.0)));
            return new AircraftViewFixture(root);
        }

        private static string CreateRoot()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xplane-737ng-view-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return root;
        }

        private static string BuildAcf(
            string name,
            string description,
            string studio,
            string version,
            string writer,
            double cgY,
            double cgZ,
            DefaultView defaultView)
        {
            return FormattableString.Invariant($"""
                1200 Version
                P acf/_descrip {description}
                P acf/_file_writer_version {writer}
                P acf/_name {name}
                P acf/_studio {studio}
                P acf/_version {version}
                P acf/_cgY {cgY:0.000000000}
                P acf/_cgZ {cgZ:0.000000000}
                P acf/_pe_xyz/0 {defaultView.XFeet:0.000000000}
                P acf/_pe_xyz/1 {defaultView.YFeet:0.000000000}
                P acf/_pe_xyz/2 {defaultView.ZFeet:0.000000000}
                P acf/_ang_offset/0,1 {defaultView.PitchDegrees:0.000000000}
                """);
        }

        private static void WritePrefs(string path, QuickView0 qv0)
        {
            WriteText(
                path,
                FormattableString.Invariant($"""
                _iql_pe_x_0 {qv0.XMeters:0.000000}
                _iql_pe_y_0 {qv0.YMeters:0.000000}
                _iql_pe_z_0 {qv0.ZMeters:0.000000}
                _iql_look_os_the_0 {qv0.PitchDegrees:0.000000}
                """));
        }

        private static void WriteText(string path, string text)
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
