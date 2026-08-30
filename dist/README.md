# Manual install (no installer)

`CamelWorks.bundle/` is a ready-to-copy Navisworks plug-in bundle. To install without
`CamelWorksSetup.exe`:

1. Build **against the Navisworks year you run** and drop the results into the matching year folder
   (`2024/`, `2025/`, `2026/`, or `2027/`):

   ```
   dotnet build CamelWorks.UI/CamelWorks.UI.csproj -c Release -p:NavisworksYear=2025
   dotnet build CamelWorks.Nav.Clash/CamelWorks.Nav.Clash.csproj -c Release -p:NavisworksYear=2025
   ```

   Copy into `CamelWorks.bundle/2025/`:

   | File | From |
   |---|---|
   | `CamelWorks.UI.dll` | `CamelWorks.UI/bin/Release/net48/` |
   | `CamelWorks.Nav.dll` | `CamelWorks.Nav/bin/Release/net48/` |
   | `CamelWorks.Nav.Clash.dll` | `CamelWorks.Nav.Clash/bin/Release/net48/` |
   | `CamelWorks.Core.dll` | `CamelWorks.Core/bin/Release/netstandard2.0/` |

   and `CamelWorks.UI/CamelWorks.xaml` into `2025/en-US/`, and `CamelWorks.UI/Resources/*.png` into
   `2025/Resources/`. The Navisworks API is restored from NuGet, so no Navisworks install is needed
   to build.

   You only need the year folder(s) for the version(s) you actually have; leave the rest empty.

   **`CamelWorks.Nav.Clash.dll` is not declared in `PackageContents.xml`, and that is deliberate.**
   It is the only assembly that needs `Autodesk.Navisworks.Clash.dll`, which ships with Navisworks
   Manage and not with Simulate. CamelWorks loads it by name the moment a clash screen is opened, so
   a Simulate user gets a screen saying the clash engine is not in their edition rather than a
   `FileNotFoundException` naming a DLL they have never heard of.

2. Copy the whole **`CamelWorks.bundle`** folder into:

   ```
   %AppData%\Autodesk\ApplicationPlugins\
   ```

3. Restart Navisworks — the **BIMCamel** ribbon tab appears, with CamelWorks on it.

## The other two plug-ins

A release built by the workflow carries three bundles — `CamelWorks.bundle`, `Dyncamelo.bundle`
and `BIMCamel.bundle` — and installs all three. Only the CamelWorks one is in this repository;
the other two are downloaded from their own repositories' latest releases at release time.

Building by hand gives you CamelWorks alone, which runs perfectly well: Automate ▸ Graphs and
Export Kit ▸ IFC each say what is missing and where to get it. To have all three, take the
release zip, or install Dyncamelo and the exporter from their own repositories — the bundles sit
side by side in `ApplicationPlugins` and share one ribbon tab either way.

## Why one DLL per year

A Navisworks plug-in must be **compiled against the API of the release it runs in** — 2024 uses
`Autodesk.Navisworks.Api` **v21**, 2025 **v22**, 2026 **v23**, 2027 **v24**. A single DLL built
against one year still *loads* in the others (it shows up in the Plugin Manager), but its **ribbon
tab silently fails to appear**, because Navisworks reflects over the `[RibbonLayout]` /
`[RibbonTab]` / `[Command]` attributes and those attribute types do not resolve across a major API
version. Hence a folder per year.

## If the ribbon tab does not appear

If Navisworks reports `PLUGIN_LOAD_02` / `0x80131515` after a manual copy, Windows kept the
browser's "downloaded file" mark on the DLLs. The setup exe strips it automatically; by hand:

```powershell
Get-ChildItem "$env:APPDATA\Autodesk\ApplicationPlugins\CamelWorks.bundle" -Recurse -File | Unblock-File
```

Works on Navisworks 2024 / 2025 / 2026 / 2027, Manage and Simulate, and on any UI language —
Navisworks falls back to the `en-US` ribbon layout. No admin rights needed.
