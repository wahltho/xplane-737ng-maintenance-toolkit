namespace LevelUp.NavTableUpdater.Core.Tests;

internal static class TestFixtures
{
    public const string ManifestText = """
        schema|package-manifest|1
        package|id|x-plane-levelup-737ng-vnav-descent-tables
        package|version|v0.2.0
        package|release_tag|v0.2.0
        aircraft|family|levelup_737ng
        repository|url|https://github.com/JT8D-17/X-Plane-LevelUp-737NG-Descent-Tables
        target|relative_path|plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua
        payload|table|B738.a_fms_levelup_tables.lua|size|22|sha256|10b27c84399c8b46dd74f49384eadd0fdb8ceed8a7f9a8b21655ca11ac258f7b
        payload|dofile|Add_dofile.txt|size|24|sha256|a6b464c8d37c9a2383953821ceb0affe127805952a54eb8b81036b452c136aef
        payload|kias|Add_to_take_alt_dist.txt|size|22|sha256|c15b590c32ff54a2553a5d3190e032e1f080efb2999a6cf21c0969182a677e54
        payload|mach|Add_to_take_alt_dist_mach.txt|size|22|sha256|054068d7ae7dd653b29d37a317653a922aec891437a1ef1e7dbf002d48b4d618
        anchor|dofile|jit.off()
        anchor|kias|function take_alt_dist(x_idx_alt, x_spd_alt, x_spd_wnd_alt, x_flap)
        anchor|mach|function take_alt_dist_mach(x_idx_alt, x_spd_alt, x_spd_wnd_alt)
        marker|dofile|begin|-- BEGIN LEVELUP_VNAV_DESCENT_TABLES DOFILE
        marker|dofile|end|-- END LEVELUP_VNAV_DESCENT_TABLES DOFILE
        marker|kias|begin|-- BEGIN LEVELUP_VNAV_DESCENT_TABLES KIAS
        marker|kias|end|-- END LEVELUP_VNAV_DESCENT_TABLES KIAS
        marker|mach|begin|-- BEGIN LEVELUP_VNAV_DESCENT_TABLES MACH
        marker|mach|end|-- END LEVELUP_VNAV_DESCENT_TABLES MACH
        legacy|v0.1.0|dofile|dofile("B738.a_fms_levelup_tables.lua")
        legacy|v0.1.0|kias|pcall(B738_variant_test_take_alt_dist,
        legacy|v0.1.0|mach|pcall(B738_variant_test_take_alt_dist_mach,
        hash_binding|upstream_b738_a_fms_lua|none
        """;

    public static readonly IReadOnlyList<(string FileName, string Content)> PayloadFiles =
    [
        ("B738.a_fms_levelup_tables.lua", "levelup table payload\n"),
        ("Add_dofile.txt", "dofile fragment payload\n"),
        ("Add_to_take_alt_dist.txt", "kias fragment payload\n"),
        ("Add_to_take_alt_dist_mach.txt", "mach fragment payload\n")
    ];

    public const string UnpatchedLua = """
        jit.off()

        function take_alt_dist(x_idx_alt, x_spd_alt, x_spd_wnd_alt, x_flap)
            local altitude_distance = 0
            return altitude_distance
        end

        function take_alt_dist_mach(x_idx_alt, x_spd_alt, x_spd_wnd_alt)
            local altitude_distance = 0
            return altitude_distance
        end
        """;

    public const string CurrentMarkedLua = """
        jit.off()
        -- BEGIN LEVELUP_VNAV_DESCENT_TABLES DOFILE
        -- package-id|x-plane-levelup-737ng-vnav-descent-tables
        -- package-version|v0.2.0
        dofile("B738.a_fms_levelup_tables.lua")
        -- END LEVELUP_VNAV_DESCENT_TABLES DOFILE

        function take_alt_dist(x_idx_alt, x_spd_alt, x_spd_wnd_alt, x_flap)
            -- BEGIN LEVELUP_VNAV_DESCENT_TABLES KIAS
            -- package-id|x-plane-levelup-737ng-vnav-descent-tables
            -- package-version|v0.2.0
            local variant_test_ok, variant_test_dist = pcall(B738_variant_test_take_alt_dist, x_idx_alt, x_spd_alt, x_spd_wnd_alt, x_flap)
            if variant_test_ok and variant_test_dist ~= nil then
                return variant_test_dist
            end
            -- END LEVELUP_VNAV_DESCENT_TABLES KIAS
            local altitude_distance = 0
            return altitude_distance
        end

        function take_alt_dist_mach(x_idx_alt, x_spd_alt, x_spd_wnd_alt)
            -- BEGIN LEVELUP_VNAV_DESCENT_TABLES MACH
            -- package-id|x-plane-levelup-737ng-vnav-descent-tables
            -- package-version|v0.2.0
            local variant_test_ok, variant_test_dist = pcall(B738_variant_test_take_alt_dist_mach, x_idx_alt, x_spd_alt, x_spd_wnd_alt)
            if variant_test_ok and variant_test_dist ~= nil then
                return variant_test_dist
            end
            -- END LEVELUP_VNAV_DESCENT_TABLES MACH
            local altitude_distance = 0
            return altitude_distance
        end
        """;

    public const string LegacyLua = """
        jit.off()
        dofile("B738.a_fms_levelup_tables.lua")

        function take_alt_dist(x_idx_alt, x_spd_alt, x_spd_wnd_alt, x_flap)
            local variant_test_ok, variant_test_dist = pcall(B738_variant_test_take_alt_dist, x_idx_alt, x_spd_alt, x_spd_wnd_alt, x_flap)
            if variant_test_ok and variant_test_dist ~= nil then
                return variant_test_dist
            end
            local altitude_distance = 0
            return altitude_distance
        end

        function take_alt_dist_mach(x_idx_alt, x_spd_alt, x_spd_wnd_alt)
            local variant_test_ok, variant_test_dist = pcall(B738_variant_test_take_alt_dist_mach, x_idx_alt, x_spd_alt, x_spd_wnd_alt)
            if variant_test_ok and variant_test_dist ~= nil then
                return variant_test_dist
            end
            local altitude_distance = 0
            return altitude_distance
        end
        """;

    public const string PartialMarkerLua = """
        jit.off()
        -- BEGIN LEVELUP_VNAV_DESCENT_TABLES DOFILE
        dofile("B738.a_fms_levelup_tables.lua")

        function take_alt_dist(x_idx_alt, x_spd_alt, x_spd_wnd_alt, x_flap)
            local altitude_distance = 0
            return altitude_distance
        end

        function take_alt_dist_mach(x_idx_alt, x_spd_alt, x_spd_wnd_alt)
            local altitude_distance = 0
            return altitude_distance
        end
        """;
}
