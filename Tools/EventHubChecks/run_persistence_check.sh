#!/usr/bin/env bash
# EventHub kalıcılık (persistence) kontrolünü derler ve çalıştırır.
# Kullanım: Tools/EventHubChecks/run_persistence_check.sh
# Gerekli: dotnet, Unity editörü (UNITY_PATH ile ezilebilir).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VER="$(grep -oP 'm_EditorVersion:\s*\K\S+' "$ROOT/ProjectSettings/ProjectVersion.txt" | head -1)"
UNITY="${UNITY_PATH:-$HOME/Unity/Hub/Editor/$VER}"

ENGINE="$UNITY/Editor/Data/Managed/UnityEngine"
MONO="$UNITY/Editor/Data/MonoBleedingEdge"
CSC="$MONO/lib/mono/msbuild/Current/bin/Roslyn/csc.exe"

if [[ ! -d "$UNITY" ]]; then
  echo "Unity bulunamadı: $UNITY (UNITY_PATH ile belirtin)" >&2
  exit 2
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

dotnet build "$ROOT/Assembly-CSharp.csproj" -nologo -v q -p:OutputPath="$WORK/" >/dev/null

NS="$(ls "$MONO"/lib/mono/*/Facades/netstandard.dll | head -1)"

"$MONO/bin/mono" "$CSC" -langversion:preview -target:exe -out:"$WORK/PersistenceCheck.exe" \
  "$ROOT/Tools/EventHubChecks/PersistenceCheck.cs" \
  -r:"$WORK/Assembly-CSharp.dll" \
  -r:"$ENGINE/UnityEngine.dll" \
  -r:"$ENGINE/UnityEngine.CoreModule.dll" \
  -r:"$NS"

MONO_PATH="$WORK:$ENGINE" "$MONO/bin/mono" "$WORK/PersistenceCheck.exe"
