using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LevelUp.NavTableUpdater.Core.Aircraft;

public static class AircraftFileParser
{
    private static readonly Regex LevelUpReleasePattern = new(
        @"\bB738DR_lvlup_rel\s*=\s*[\""']([^\""']+)[\""']",
        RegexOptions.CultureInvariant);

    private static readonly Regex VersionLinePattern = new(
        @"^Version\s+(.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static AcfMetadata ReadAcfMetadata(string acfPath)
    {
        string? name = null;
        string? description = null;
        string? studio = null;
        string? acfVersion = null;
        string? fileWriterVersion = null;
        double? cgY = null;
        double? cgZ = null;
        double? viewX = null;
        double? viewY = null;
        double? viewZ = null;
        double? viewPitch = null;

        foreach (var line in File.ReadLines(acfPath))
        {
            var parts = SplitPropertyLine(line);
            if (parts is null || parts.Value.Prefix != "P")
            {
                continue;
            }

            switch (parts.Value.Key)
            {
                case "acf/_name":
                    name = parts.Value.Value;
                    break;
                case "acf/_descrip":
                    description = parts.Value.Value;
                    break;
                case "acf/_studio":
                    studio = parts.Value.Value;
                    break;
                case "acf/_version":
                    acfVersion = parts.Value.Value;
                    break;
                case "acf/_file_writer_version":
                    fileWriterVersion = parts.Value.Value;
                    break;
                case "acf/_cgY":
                    cgY = ParseDouble(parts.Value.Value);
                    break;
                case "acf/_cgZ":
                    cgZ = ParseDouble(parts.Value.Value);
                    break;
                case "acf/_pe_xyz/0":
                    viewX = ParseDouble(parts.Value.Value);
                    break;
                case "acf/_pe_xyz/1":
                    viewY = ParseDouble(parts.Value.Value);
                    break;
                case "acf/_pe_xyz/2":
                    viewZ = ParseDouble(parts.Value.Value);
                    break;
                case "acf/_ang_offset/0,1":
                    viewPitch = ParseDouble(parts.Value.Value);
                    break;
            }
        }

        var cg = cgY.HasValue && cgZ.HasValue ? new AircraftCg(cgY.Value, cgZ.Value) : null;
        var defaultView = viewX.HasValue && viewY.HasValue && viewZ.HasValue && viewPitch.HasValue
            ? new DefaultView(viewX.Value, viewY.Value, viewZ.Value, viewPitch.Value)
            : null;

        return new AcfMetadata(
            name,
            description,
            studio,
            acfVersion,
            fileWriterVersion,
            cg,
            defaultView);
    }

    public static QuickView0? ReadQuickView0(string prefsPath)
    {
        double? x = null;
        double? y = null;
        double? z = null;
        double? pitch = null;

        foreach (var line in File.ReadLines(prefsPath))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            switch (parts[0])
            {
                case "_iql_pe_x_0":
                    x = ParseDouble(parts[1]);
                    break;
                case "_iql_pe_y_0":
                    y = ParseDouble(parts[1]);
                    break;
                case "_iql_pe_z_0":
                    z = ParseDouble(parts[1]);
                    break;
                case "_iql_look_os_the_0":
                    pitch = ParseDouble(parts[1]);
                    break;
            }
        }

        return x.HasValue && y.HasValue && z.HasValue && pitch.HasValue
            ? new QuickView0(x.Value, y.Value, z.Value, pitch.Value)
            : null;
    }

    public static string? ReadVersionTxt(string aircraftFolder)
    {
        var versionPath = Path.Combine(aircraftFolder, "version.txt");
        if (!File.Exists(versionPath))
        {
            return null;
        }

        var version = File.ReadLines(versionPath).FirstOrDefault();
        return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
    }

    public static string? ReadLevelUpVersion(string aircraftFolder)
    {
        var candidates = new[]
        {
            Path.Combine(aircraftFolder, "plugins", "xlua", "scripts", "LU_737NG.sound", "LU_737NG.sound.lua"),
            Path.Combine(aircraftFolder, "plugins", "xlua", "scripts", "B738.LevelUp.sound", "B738.LevelUp.sound.lua")
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            foreach (var line in File.ReadLines(candidate))
            {
                var match = LevelUpReleasePattern.Match(line);
                if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                {
                    return match.Groups[1].Value.Trim();
                }
            }
        }

        var packageVersion = ReadLevelUpPackageVersion(aircraftFolder);
        if (!string.IsNullOrWhiteSpace(packageVersion))
        {
            return packageVersion;
        }

        return ReadVersionTxt(aircraftFolder);
    }

    private static string? ReadLevelUpPackageVersion(string aircraftFolder)
    {
        var versionPath = Path.Combine(aircraftFolder, "LU & Zibo Version.txt");
        if (!File.Exists(versionPath))
        {
            return null;
        }

        var inLevelUpSection = false;
        foreach (var line in File.ReadLines(versionPath))
        {
            var value = line.Trim().TrimStart('\uFEFF');
            if (value.StartsWith("LevelUp", StringComparison.OrdinalIgnoreCase))
            {
                inLevelUpSection = true;
                continue;
            }

            if (!inLevelUpSection || value.Length == 0)
            {
                continue;
            }

            var match = VersionLinePattern.Match(value);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                return match.Groups[1].Value.Trim();
            }

            if (value.EndsWith(':'))
            {
                break;
            }
        }

        return null;
    }

    public static AircraftMaintenanceMetadata? ReadMaintenanceMetadata(string aircraftFolder, out string? error)
    {
        error = null;
        var metadataPath = Path.Combine(aircraftFolder, AircraftMaintenanceMetadata.FileName);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<AircraftMaintenanceMetadata>(
                File.ReadAllText(metadataPath),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (metadata is null)
            {
                error = $"{AircraftMaintenanceMetadata.FileName} is empty.";
                return null;
            }

            return metadata;
        }
        catch (JsonException ex)
        {
            error = $"{AircraftMaintenanceMetadata.FileName} is invalid JSON: {ex.Message}";
            return null;
        }
        catch (IOException ex)
        {
            error = $"{AircraftMaintenanceMetadata.FileName} could not be read: {ex.Message}";
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = $"{AircraftMaintenanceMetadata.FileName} could not be read: {ex.Message}";
            return null;
        }
    }

    public static DefaultView CalculateDefaultViewFromQuickView(AircraftCg cg, QuickView0 quickView)
    {
        const double metersToFeet = 3.28084;
        return new DefaultView(
            XFeet: quickView.XMeters * metersToFeet,
            YFeet: cg.YFeet + quickView.YMeters * metersToFeet,
            ZFeet: cg.ZFeet + quickView.ZMeters * metersToFeet,
            PitchDegrees: quickView.PitchDegrees);
    }

    private static (string Prefix, string Key, string Value)? SplitPropertyLine(string line)
    {
        var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3 ? (parts[0], parts[1], parts[2]) : null;
    }

    private static double? ParseDouble(string value)
    {
        return double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
