#!/usr/bin/env bash
# Type-check CamelWorks.UI on Linux, where its SDK does not exist.
#
# CamelWorks.UI is net48 + WPF and builds through Microsoft.NET.Sdk.WindowsDesktop, which ships
# only on Windows -- so "dotnet build" on this repo's UI assembly cannot work here, and for a long
# time that meant every UI change was pushed unverified and a missing brace arrived as a CI failure
# twenty minutes later. The hosted matrix also does not run on pull requests, by budget choice, so
# there was no compiler in the loop at all.
#
# The SDK is not needed to type-check. The net48 reference assemblies carry PresentationFramework,
# PresentationCore, WindowsBase and System.Windows.Forms; the Navisworks API package carries the
# host assemblies; and Roslyn ships with the Linux SDK. So this hands csc every .cs file in the
# assembly against those references and reports what it says.
#
# What this does NOT do, and what still needs a Windows runner:
#   * Compile XAML. SkinSheet.xaml declares x:Class, so the real build generates a partial with
#     InitializeComponent(); a stub stands in for it here, which type-checks the C# that calls it
#     but says nothing about whether the markup is valid.
#   * Produce a usable assembly. This is a check, not a build: output goes to a temp file.
#
# Usage: checks/typecheck-ui.sh [year]   (default 2024)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
YEAR="${1:-2024}"

case "$YEAR" in
  2024) PKG=2024.0.0 ;;
  2025) PKG=2025.0.0 ;;
  2026) PKG=2026.0.1 ;;
  2027) PKG=2027.0.0 ;;
  *) echo "unknown Navisworks year: $YEAR" >&2; exit 2 ;;
esac

REFDIR="$(echo "$HOME"/.nuget/packages/microsoft.netframework.referenceassemblies.net48/*/build/.NETFramework/v4.8)"
NAVDIR="$HOME/.nuget/packages/speckle.navisworks.api/$PKG/lib/net48"
CSC="$(find /usr/lib/dotnet/sdk -name csc.dll | head -1)"

for d in "$REFDIR" "$NAVDIR"; do
  [ -d "$d" ] || { echo "missing references: $d" >&2
                   echo "run: dotnet build CamelWorks.Nav/CamelWorks.Nav.csproj -p:NavisworksYear=$YEAR" >&2
                   exit 2; }
done

# The dependencies, built through the ordinary SDK: both are plain class libraries.
dotnet build "$ROOT/CamelWorks.Core/CamelWorks.Core.csproj" -c Release -v q --nologo >/dev/null
dotnet build "$ROOT/CamelWorks.Nav/CamelWorks.Nav.csproj" -c Release -v q --nologo \
  -p:NavisworksYear="$YEAR" >/dev/null

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Stands in for what the XAML compiler generates from SkinSheet.xaml's x:Class.
cat > "$WORK/SkinSheet.g.cs" <<'STUB'
namespace CamelWorks.UI.Themes
{
    public partial class SkinSheet
    {
        // The real one is generated from SkinSheet.xaml by the XAML compiler, which needs Windows.
        public void InitializeComponent() { }
    }
}
STUB

REFS=()

# Two of the reference "assemblies" are native: System.EnterpriseServices.Wrapper and .Thunk are
# PE images with no managed metadata, and csc refuses the whole compilation over either of them.
for dll in "$REFDIR"/*.dll "$REFDIR"/Facades/*.dll; do
  case "$(basename "$dll")" in
    System.EnterpriseServices.Wrapper.dll|System.EnterpriseServices.Thunk.dll) continue ;;
  esac
  REFS+=("-r:$dll")
done
for dll in "$NAVDIR"/*.dll; do REFS+=("-r:$dll"); done
REFS+=("-r:$ROOT/CamelWorks.Core/bin/Release/netstandard2.0/CamelWorks.Core.dll")
REFS+=("-r:$ROOT/CamelWorks.Nav/bin/Release/net48/CamelWorks.Nav.dll")

mapfile -t SOURCES < <(find "$ROOT/CamelWorks.UI" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*')

# Matched to the csproj: warnings are errors there, so they are errors here or this check passes
# things the real build rejects. NAV<year> is the project's only #if.
dotnet "$CSC" \
  -nologo -noconfig -nostdlib \
  -target:library \
  -out:"$WORK/CamelWorks.UI.dll" \
  -langversion:10.0 \
  -nullable:enable \
  -warnaserror \
  -define:"NAV$YEAR" \
  -platform:x64 \
  "${REFS[@]}" \
  "$WORK/SkinSheet.g.cs" \
  "${SOURCES[@]}"

echo "CamelWorks.UI type-checks against Navisworks $YEAR (${#SOURCES[@]} files)"
