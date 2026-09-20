# X-Plane.org Listing Copy

Open the HTML companion in a browser and copy the rendered description below "Full Description" into the forum editor. Keep release numbers and release notes in the separate forum release fields.

## Title

X-Plane 737NG Maintenance Toolkit

## Short Description

Install and update supported Zibo and LevelUp aircraft in X-Plane 12, apply maintenance patches, manage optional tools, and maintain views and hardware settings.

## Full Description

The **X-Plane 737NG Maintenance Toolkit (MTK)** helps you install, update and maintain supported **Zibo 737-800X and LevelUp 737NG** aircraft in **X-Plane 12**. It brings aircraft updates, required maintenance patches, optional add-ons and configuration tools into one application.

## Updating an existing aircraft

1. Close X-Plane completely and start the Toolkit.
2. Select your aircraft installation. Use **Auto-detect** or **Browse** and **Scan selected folder** if it is not already selected.
3. Check the **Installed**, **Available** and maintenance status shown on the Start page. Aircraft and patch releases are checked automatically at startup when that setting is enabled.
4. Click **Update**, review the proposed changes and confirm.
5. Wait for the final result before starting X-Plane again.

**For LevelUp, the same Update action applies the aircraft update and then its required maintenance patches automatically. There is no separate confirmation for those required patches.** If the aircraft package is already current, Update can still bring its maintenance patches up to date. Previously selected optional modules are retained.

Existing installations can be updated directly; a clean reinstall is not a routine requirement. Modified or incomplete installations may require attention if the Toolkit cannot safely validate the affected files or their restore history.

## Installing a new aircraft

Use **Install new aircraft** on the Start page, choose Zibo or LevelUp, select an unused aircraft destination under your X-Plane installation, then use **Check package** and **Install**.

The Toolkit obtains the required full aircraft package and cumulative update from the supported source. It prepares and validates the files before activating the new folder. For LevelUp, required maintenance patches follow automatically. The new installation is selected when the operation finishes.

Zibo downloads use the official Skymatix feed. Where that feed requires BitTorrent, the Toolkit asks before starting a peer-to-peer download; peers can see your IP address and the client may upload package pieces during the transfer. LevelUp packages come from its authorized public GitHub release source. **Import package** is available for supported packages downloaded separately.

## LevelUp maintenance patches

The required group contains:

- **VNAV Descent Tables**
- **Weight & Balance**
- **FANS CDU** — the switchable CDU labels and tablet selection

The Toolkit checks the patch repositories for current releases and validates the selected modules before applying them. It recognizes supported existing patches and avoids rewriting files that already match the required state.

Optional aircraft patches include:

- **Tablet Performance Calculator** for LevelUp
- **AUTO JETWAY** for supported Zibo and LevelUp aircraft
- **CPDLC FANS Pages** for supported Zibo and LevelUp aircraft

**FANS CDU and CPDLC FANS Pages are different patches.** FANS CDU belongs to the required LevelUp group; CPDLC FANS Pages is optional. Optional patches are installed only when selected. If you previously installed a patch manually, follow that patch's migration instructions before switching to Toolkit management.

Zibo also has a separate VNAV Descent Tables maintenance workflow.

## Optional components, tools and resources

Available packages are filtered for the selected aircraft and, where applicable, the operating system and architecture. They include:

- **Optimized XLua:** installed per aircraft while preserving its aircraft-owned Lua scripts.
- **Yet Another Linda (YAL), YAL HoppieHelper and YANSH:** optional tools installed once per X-Plane installation.
- **XLinSpeak:** an optional speech plugin for Linux x64. Piper, a voice model and audio output must be installed and configured separately; the Toolkit installs the plugin only.
- **737NG Realbench Logger:** optional logging tools, with unrelated profiles and generated logs preserved.
- **LevelUp Paintkit:** verified download and extraction into a folder you choose.

Supported components and tools have install, update, repair and restore actions. Stable and beta channels are offered where the package supports them.

The online catalog can add or change available packages without rebuilding the application when the existing Toolkit supports their requirements. Some additions require a Toolkit update. Older Toolkits retain a compatible cached or bundled catalog when a newer catalog cannot be used.

## Views and hardware settings

- Correct saved Quick Views after an aircraft centre-of-gravity change, with support for a matching X-Camera configuration.
- Use Quick View 0 as the default viewpoint.
- Copy Quick Views between variants in one LevelUp installation, with CG correction and a choice about replacing default viewpoints.
- Copy saved hardware configurations between detected Zibo and LevelUp variants within the same X-Plane installation, with backup and restore.

## Download and compatibility

Supports X-Plane 12, Zibo 737-800X 2K/4K and the LevelUp 737-600, 737-700, 737-800, 737-900 and 737-900ER.

Get the current release using the links below:

- **Windows x64:** [Installer](https://github.com/wahltho/xplane-737ng-maintenance-toolkit/releases/latest/download/XPlane737NGMaintenanceToolkit-stable-win-x64-Setup.exe) or [portable ZIP](https://github.com/wahltho/xplane-737ng-maintenance-toolkit/releases/latest/download/XPlane737NGMaintenanceToolkit-stable-win-x64-Portable.zip).
- **macOS Apple silicon:** [Installer](https://github.com/wahltho/xplane-737ng-maintenance-toolkit/releases/latest/download/XPlane737NGMaintenanceToolkit-stable-osx-arm64-Setup.pkg) or [portable ZIP](https://github.com/wahltho/xplane-737ng-maintenance-toolkit/releases/latest/download/XPlane737NGMaintenanceToolkit-stable-osx-arm64-Portable.zip).
- **Linux x64:** [AppImage](https://github.com/wahltho/xplane-737ng-maintenance-toolkit/releases/latest/download/XPlane737NGMaintenanceToolkit-stable-linux-x64.AppImage). Make the downloaded file executable before launching it.

For checksums and release notes, visit the [latest release page](https://github.com/wahltho/xplane-737ng-maintenance-toolkit/releases/latest). Use the installer, portable ZIP or AppImage for a manual installation; the other package and feed files support the built-in updater. The macOS build is for Apple silicon. X-Plane 11 is not supported.

The Windows and macOS builds are unsigned, and the macOS build is not notarized. Verify the download against the release checksums. If macOS blocks a verified download, follow [Apple's Open Anyway instructions](https://support.apple.com/guide/mac-help/open-a-mac-app-from-an-unknown-developer-mh40616/mac).

## Toolkit updates and settings

The Toolkit can notify you when a newer application release is available. Downloading and applying that update requires your action. In **Settings**, you can independently disable startup checks for **Toolkit updates** and for **aircraft and patch releases**. Manual checks remain available.

## Backups and support

The Toolkit validates packages and affected files before writing, creates backups for changes to existing installations and preserves protected preferences and unrelated files. Restore depends on the available backup history. Keep your own backups as well.

If an operation reports **blocked** or **required patches pending**, check the final message before flying. An aircraft update may have completed while its required patches remain unfinished. **Do not delete Toolkit settings or backups to bypass an error.**

To request help, open **Advanced → Dump to file**. The export dialog shows the saved log's full path; **Open folder** takes you to it. Attach the complete `.txt` log, describe what you were doing and mention any manual aircraft modifications. A screenshot can help, but does not replace the log.

[User manual](https://github.com/wahltho/xplane-737ng-maintenance-toolkit/blob/main/docs/USER_MANUAL.md) · [Source code and issue tracker](https://github.com/wahltho/xplane-737ng-maintenance-toolkit)

The MTK is an independent community project, not an official Laminar Research, Zibo or LevelUp product. Its source is available under the MIT License; the application is provided without warranty.
