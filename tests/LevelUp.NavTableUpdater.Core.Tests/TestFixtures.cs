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
        payload|table|B738.a_fms_levelup_tables.lua|size|22051|sha256|d79ef7c2c7c613ae51547a44dbefb6667cdce47d238cc595a4c34d66a643b516
        payload|dofile|Add_dofile.txt|size|208|sha256|1b7e76e0530a6a54153ad17a54db18aa83e19746f8cacea03aa1caadce45e313
        payload|kias|Add_to_take_alt_dist.txt|size|382|sha256|18d311823f15790f2a9b2b42b48180d79ff8721a59d7b1899feedf9570a5b0db
        payload|mach|Add_to_take_alt_dist_mach.txt|size|379|sha256|2502f24b03a045e7bd22653f9372a2a1a6fa7e60d4e7f7c3e6365b03d672fac5
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
