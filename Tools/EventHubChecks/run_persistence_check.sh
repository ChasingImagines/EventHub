#!/usr/bin/env bash
# EventHub kalıcılık (persistence) kontrolünü derler ve çalıştırır.
# Kullanım: Tools/EventHubChecks/run_persistence_check.sh
# Gerekli: mono + Unity editörü (UNITY_PATH ile ezilebilir).
#
# Not: Unity'nin ürettiği Assembly-CSharp.csproj'a bağımlı DEĞİLDİR; kaynakları doğrudan derler.
#
# ÖNEMLİ: derleme UNITY_EDITOR tanımlı OLMADAN yapılır; yani player build derlemesini de taklit eder.
# Editörde UNITY_EDITOR hep tanımlı olduğu için "#if UNITY_EDITOR içinde tanımlı ama dışında
# kullanılan" üyeler Unity'de görünmez, build'de kırılır — bu script onu yakalar.
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

# --- derlenecek kaynaklar ---
SOURCES=("$ROOT"/Assets/Scripts/EventHub/Runtime/*.cs)
SOURCES+=("$ROOT/Tools/EventHubChecks/PersistenceCheck.cs")

# UPM paketi (Library/PackageCache altında hash'li klasör) Runtime kaynakları
PKG="$(find "$ROOT/Library/PackageCache" -maxdepth 1 -type d -name '*mackysoft*' | head -1)"
if [[ -z "$PKG" ]]; then
  echo "MackySoft paketi bulunamadı (Unity'de paket çözülmemiş olabilir)." >&2
  exit 2
fi
SOURCES+=("$PKG"/Runtime/*.cs)

# --- referanslar: tüm UnityEngine modülleri + netstandard ---
REFS=()
for dll in "$ENGINE"/*.dll; do REFS+=("-r:$dll"); done
REFS+=("-r:$(ls "$MONO"/lib/mono/*/Facades/netstandard.dll | head -1)")

"$MONO/bin/mono" "$CSC" -langversion:preview -target:exe -out:"$WORK/PersistenceCheck.exe" \
  "${REFS[@]}" "${SOURCES[@]}"

MONO_PATH="$WORK:$ENGINE" "$MONO/bin/mono" "$WORK/PersistenceCheck.exe"
