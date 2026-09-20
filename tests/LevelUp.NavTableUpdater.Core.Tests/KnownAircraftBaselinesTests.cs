using System.Security.Cryptography;
using System.Text;
using LevelUp.NavTableUpdater.Core.Content;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class KnownAircraftBaselinesTests
{
    [Fact]
    public void ExactEvidence_IsScopedToProductPathSizeAndBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("official");
        var entry = new KnownAircraftBaseline("levelup-737ng", "release", "objects/test.obj", bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)), "https://example.org/manifest", new string('a', 64));
        var catalog = new KnownAircraftBaselines([entry]);
        Assert.Equal(entry, catalog.Match("levelup-737ng", "objects/test.obj", bytes));
        Assert.Null(catalog.Match("zibo-737ng", "objects/test.obj", bytes));
        Assert.Null(catalog.Match("levelup-737ng", "objects/other.obj", bytes));
        Assert.Null(catalog.Match("levelup-737ng", "objects/test.obj", Encoding.UTF8.GetBytes("officiaL")));
        Assert.Null(catalog.Match("levelup-737ng", "objects/test.obj", [.. bytes, 0]));
    }

    [Fact]
    public void BundledEvidence_IsLoadedButNeverMatchesUnverifiedBytes()
    {
        Assert.Null(KnownAircraftBaselines.BuiltIn.Match("levelup-737ng", "objects/737_cockpit_ovhd2.obj", [1, 2, 3]));
    }
}
