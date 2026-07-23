using LevelUp.NavTableUpdater.Core.Detection;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class AircraftDetectorTests
{
    [Fact]
    public void FindCandidates_WhenLinuxInstallFilePointsToXPlaneRoot_ReturnsNestedZiboAircraft()
    {
        using var fixture = DetectorFixture.Create();
        var xplaneRoot = Path.Combine(fixture.RootPath, "media", "X-Plane", "X-Plane_12.4");
        var aircraftRoot = Path.Combine(xplaneRoot, "Aircraft", "AddOn_Aircraft", "B737-800X");
        Directory.CreateDirectory(aircraftRoot);
        DetectorFixture.WriteZiboAcf(aircraftRoot);
        fixture.WriteLinuxInstallFile(xplaneRoot);

        var candidates = new AircraftDetector(fixture.HomePath).FindCandidates();

        var candidate = Assert.Single(candidates);
        Assert.Equal("B737-800X", candidate.Name);
        Assert.Equal(Path.GetFullPath(aircraftRoot), candidate.Path);
        Assert.Contains("Zibo 737-800", candidate.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void FindCandidates_WhenAdditionalRootIsDirectAircraftFolder_ReturnsThatAircraft()
    {
        using var fixture = DetectorFixture.Create();
        var aircraftRoot = Path.Combine(fixture.RootPath, "Custom", "B737-800X");
        Directory.CreateDirectory(aircraftRoot);
        DetectorFixture.WriteZiboAcf(aircraftRoot);

        var candidates = new AircraftDetector(fixture.HomePath).FindCandidates([aircraftRoot]);

        var candidate = Assert.Single(candidates);
        Assert.Equal(Path.GetFullPath(aircraftRoot), candidate.Path);
    }

    private sealed class DetectorFixture : IDisposable
    {
        private DetectorFixture(string rootPath)
        {
            RootPath = rootPath;
            HomePath = Path.Combine(rootPath, "home");
            Directory.CreateDirectory(HomePath);
        }

        public string RootPath { get; }

        public string HomePath { get; }

        public static DetectorFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"xplane-737ng-detector-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new DetectorFixture(root);
        }

        public void WriteLinuxInstallFile(string xplaneRoot)
        {
            var installDir = Path.Combine(HomePath, ".x-plane");
            Directory.CreateDirectory(installDir);
            File.WriteAllText(Path.Combine(installDir, "x-plane_install_12.txt"), $"{xplaneRoot}{Environment.NewLine}");
        }

        public static void WriteZiboAcf(string aircraftRoot)
        {
            File.WriteAllText(
                Path.Combine(aircraftRoot, "b738.acf"),
                """
                1200 Version
                P acf/_descrip Boeing 737-800X
                P acf/_file_writer_version 124201
                P acf/_name Boeing 737-800X
                P acf/_studio Laminar Research modified by Zibo and Twkster
                P acf/_version ver 4.05
                P acf/_cgY -2.000000000
                P acf/_cgZ 60.340000153
                P acf/_pe_xyz/0 0.000000000
                P acf/_pe_xyz/1 0.000000000
                P acf/_pe_xyz/2 0.000000000
                P acf/_ang_offset/0,1 0.000000000
                """);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
