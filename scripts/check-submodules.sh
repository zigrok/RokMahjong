#!/usr/bin/env bash
# Verify that every submodule commit pinned by this repository is reachable from
# a remote branch, so CI and other clones can fetch it.
#
# Also checks that the submodule worktrees match the superproject gitlinks.
set -euo pipefail

root=$(git rev-parse --show-toplevel 2>/dev/null) || {
  echo "error: not inside a Git repository" >&2
  exit 2
}
cd "$root"

failures=0

check_reachable() {
  local path=$1
  local label=$2

  if ! git -C "$path" rev-parse --git-dir >/dev/null 2>&1; then
    echo "FAIL $label: not initialized"
    failures=$((failures + 1))
    return
  fi

  local sha
  sha=$(git -C "$path" rev-parse HEAD)

  if ! git -C "$path" fetch --quiet origin; then
    echo "FAIL $label: could not fetch origin"
    failures=$((failures + 1))
    return
  fi

  local refs
  refs=$(git -C "$path" branch -r --contains "$sha" 2>/dev/null || true)
  if [ -z "$refs" ]; then
    echo "FAIL $label: $sha is not reachable from any remote branch"
    echo "      Push the feature branch (or merge it upstream) before pinning it."
    failures=$((failures + 1))
    return
  fi

  local first
  first=$(printf '%s\n' "$refs" | head -n 1 | tr -d ' ')
  echo "OK   $label: ${sha:0:12} reachable from $first"
}

check_pin_matches_index() {
  local path=$1
  local label=$2
  local status
  status=$(git submodule status -- "$path" | cut -c1)
  case "$status" in
    ''|' ')
      ;;
    '-')
      echo "FAIL $label: submodule is not initialized"
      failures=$((failures + 1))
      ;;
    '+')
      echo "FAIL $label: worktree HEAD differs from the superproject gitlink"
      failures=$((failures + 1))
      ;;
    'U')
      echo "FAIL $label: submodule has an unresolved merge conflict"
      failures=$((failures + 1))
      ;;
    *)
      echo "WARN $label: unexpected submodule status '$status'"
      ;;
  esac
}

paths=()
while IFS= read -r path; do
  [ -n "$path" ] && paths+=("$path")
done < <(git config -f .gitmodules --get-regexp '^submodule\..*\.path$' | awk '{print $2}')

if [ ${#paths[@]} -eq 0 ]; then
  echo "No submodules configured."
  exit 0
fi

for path in "${paths[@]}"; do
  check_pin_matches_index "$path" "$path"
  check_reachable "$path" "$path"
done

# Forma requires XamlX for its FHTML/XAML build. ThorVG is optional.
xamlx="third_party/Forma/external/XamlX"
if [ -e "$xamlx/.git" ]; then
  check_reachable "$xamlx" "$xamlx"
else
  echo "FAIL $xamlx: not initialized (run: git -C third_party/Forma submodule update --init external/XamlX)"
  failures=$((failures + 1))
fi

if [ -e "third_party/Forma/external/ThorVG/.git" ]; then
  check_reachable "third_party/Forma/external/ThorVG" "third_party/Forma/external/ThorVG"
else
  echo "SKIP third_party/Forma/external/ThorVG: optional and not initialized"
fi

if [ "$failures" -ne 0 ]; then
  echo
  echo "$failures submodule check(s) failed."
  exit 1
fi

echo
echo "All pinned submodule commits are reachable."
