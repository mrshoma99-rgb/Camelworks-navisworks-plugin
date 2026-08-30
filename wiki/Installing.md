# Installing

## The installer

Download `CamelWorksSetup.exe` from the repository's **latest release** and run it.

- Installs **per user**, into `%APPDATA%\Autodesk\ApplicationPlugins\CamelWorks.bundle`.
- **No admin rights** needed.
- Registers an Apps & Features entry, so it uninstalls like anything else.
- Strips the "downloaded from the internet" mark off the files, which is the usual cause of a
  plug-in loading but showing no ribbon tab.

Start Navisworks and the **BIMCamel** tab appears, with CamelWorks on it.

### Three plug-ins, one installer

The installer carries three Autodesk bundles and installs all of them:

| Bundle | What it is |
|---|---|
| `CamelWorks.bundle` | The twelve apps in this guide |
| `Dyncamelo.bundle` | The Dyncamelo node editor and its engine |
| `BIMCamel.bundle` | The IFC exporter |

They stay separate bundles — each has its own manifest — and share one ribbon tab, which
CamelWorks merges at startup. Each CamelWorks release folds in whatever Dyncamelo and the exporter
had published at the time; the release notes say which versions went in.

**If you already have one of them installed**, CamelWorks replaces that bundle with the version it
ships and leaves its Add/Remove Programs entry alone. Uninstalling CamelWorks removes all three
bundles.

## What it runs in

| | |
|---|---|
| Navisworks | 2024, 2025, 2026, 2027 |
| Edition | Manage and Simulate (see the note below) |
| UI language | Any — Navisworks falls back to the `en-US` ribbon layout |

The bundle ships one folder per year and Navisworks loads the matching one. The installer probes
your machine and tells you which years it found; it warns rather than refuses if it finds none.

### Manage and Simulate

Everything works on both, **except the clash engine**, which ships with Manage and not with
Simulate. On Simulate the clash screens open and say so in a sentence, rather than failing with an
error naming a DLL you have never heard of.

Simulate users are not shut out of the clash workflow. CamelWorks can read a clash board out of an
exported XML file, work it, and write the responses back — see
[the offline review round](Common-workflows#the-offline-review-round).

## Installing by hand

If you would rather not run an installer, or you are deploying to many machines:

1. Take `CamelWorks.bundle/` from the repository (or build it — see [For developers](For-developers)).
2. Copy the whole folder into `%APPDATA%\Autodesk\ApplicationPlugins\`.
3. Unblock the files — Windows keeps a mark on anything that arrived from a browser:

   ```powershell
   Get-ChildItem "$env:APPDATA\Autodesk\ApplicationPlugins\CamelWorks.bundle" -Recurse -File | Unblock-File
   ```

4. Restart Navisworks.

You only need the year folders for the versions you actually have; leave the rest empty.

## Uninstalling

Apps & Features → **CamelWorks** → Uninstall. Or delete
`%APPDATA%\Autodesk\ApplicationPlugins\CamelWorks.bundle`.

Nothing is left behind in the registry beyond the Apps & Features entry. What CamelWorks wrote into
your project — the `.cw` project file beside the document — stays where it is, and is plain JSON.

## Where CamelWorks keeps things

| What | Where |
|---|---|
| The project file — profile, saved sets, baselines, issues, jobs | Beside the open document, as `<document>.cw` |
| Baselines for Compare | A folder beside the document |
| Per-tool memory (last folder, last pattern) | The project file |
| Anything written onto elements | A CamelWorks property tab on the element itself |

The project file travels with the job, so whoever opens it next has the same sets, the same
baselines and the same issue register. It is not in your user profile.

If two people have the same job open, the second gets a strip across the top saying somebody else
holds it, with **Take it over** on it. On a synced folder (OneDrive, Dropbox) the lock is not
trusted and saving carries on regardless.
