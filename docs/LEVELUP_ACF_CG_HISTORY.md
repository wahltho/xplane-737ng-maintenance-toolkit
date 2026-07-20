# LevelUp ACF CG History

The toolkit uses this catalog to choose the correct Quick View/default-view
reference CG for known LevelUp 737NG upstream aircraft states.

Source inspected: `petrolpram/737NG-Series` `main` at
`c2ef1e1a91896aadee8a415fcdefbb7933f013b8`, with tags visible to the repository
checkout listed below. The catalog is a reference input only. It
does not authorize writes by itself; the write path still requires local
validation, backup and explicit user action.

## Lookup Rules

For LevelUp targets, the app resolves the reference CG in this order:

1. `xplane-737ng-maintenance.json` `upstreamSourceRef`.
2. `xplane-737ng-maintenance.json` `upstreamReleaseTag`.
3. The local ACF `acf/_version` value when it is unique for the detected
   variant.
4. The current built-in baseline when no known historical source can be proven.

Short commit prefixes are accepted for `upstreamSourceRef`. The field may also
contain a repository-qualified value such as
`petrolpram/737NG-Series@2723547`.

The LevelUp 737-800 intermediate history reused `acf/_version` value
`XP12 FM 2.0.3` across several different CG values. The app therefore does not
infer those intermediate 737-800 baselines from ACF version alone. A
maintenance metadata file with `upstreamSourceRef` or `upstreamReleaseTag` is
required for those states.

## Metadata Fields

Custom distributions can preserve upstream provenance with:

```json
{
  "schemaVersion": 1,
  "aircraftFamily": "levelup-737ng",
  "variant": "levelup-737-800",
  "distribution": "wahltho-no-lua-port",
  "distributionVersion": "5.00.00",
  "upstreamFamily": "levelup-737ng",
  "upstreamSourceRef": "petrolpram/737NG-Series@2723547",
  "upstreamReleaseTag": "v2.S1.01",
  "runtime": "no-lua-cpp"
}
```

Only one of `upstreamSourceRef` or `upstreamReleaseTag` is needed. A commit is
more precise than a release tag.

## Known LevelUp Values

| Variant | Source | `acf/_cgY` ft | `acf/_cgZ` ft | Match hints |
| --- | --- | ---: | ---: | --- |
| 737-600 | through `v2.S1.01` | -2.049999952 | 46.139999390 | `v2-alpha10`, `v2.S1.01`, `XP12 FM 2.0.3` |
| 737-600 | `f76e128` through `c2ef1e1a` | -2.049999952 | 46.040000916 | `v2.S1.50B`, `v2.S1.50C`, `XP12 V2.S1.50B (20260707-2115 SAO)` |
| 737-700 | through `v2.S1.01` | -2.049999952 | 50.840000153 | `v2-alpha10`, `v2.S1.01`, `XP12 FM 2.0.3` |
| 737-700 | `f76e128` through `c2ef1e1a` | -2.049999952 | 49.740001678 | `v2.S1.50B`, `v2.S1.50C`, `XP12 2.S1.50B (20260709-2031 SAO)` |
| 737-800 | `v2-alpha10` | -2.049999952 | 60.220001221 | `v2-alpha10`, source ref required for commit-only installs |
| 737-800 | `36f9fe5` | -1.049999952 | 59.500000000 | source ref required |
| 737-800 | `v2.S1.01` / `2723547` | -1.049999952 | 60.299999237 | `v2.S1.01` or source ref required |
| 737-800 | `4a0bb70` | -1.049999952 | 60.220001221 | source ref required |
| 737-800 | `f76e128` through `c2ef1e1a` | -2.049999952 | 60.220001221 | `v2.S1.50B`, `v2.S1.50C`, `XP12 FM V2.S1.50B (20260712-1919 SAO)` |
| 737-900 | through `v2.S1.01` | -2.049999952 | 66.339996338 | `v2-alpha10`, `v2.S1.01`, `XP12 FM 2.0.3` |
| 737-900 | `v2.S1.50B` | -2.049999952 | 65.650001526 | `v2.S1.50B` or source ref required |
| 737-900 | `ff32156` through `c2ef1e1a` | -2.049999952 | 65.800003052 | `v2.S1.50C`, `XP12 V2.S1.50B (20260711-2137 SAO)` |
| 737-900ER | through `v2.S1.01` | -2.049999952 | 66.339996338 | `v2-alpha10`, `v2.S1.01`, `XP12 FM 2.0.3` |
| 737-900ER | `f76e128` through `c2ef1e1a` | -2.049999952 | 65.800003052 | `v2.S1.50B`, `v2.S1.50C`, `XP12 FM V2.S1.50B (20260712-1921 SAO)` |

## Safety Boundary

This catalog is used to select a baseline for analysis and view maintenance.
If a target cannot be matched to a known LevelUp state, the app keeps the
current built-in baseline and reports any CG delta instead of silently adopting
unknown aircraft values.
