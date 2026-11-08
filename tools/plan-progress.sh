#!/usr/bin/env bash
# Count checklist progress in a Markdown plan.
#
# A checklist item is a line matching: - [ ] (open), - [~] (in progress) or
# - [x] / - [X] (done). Items are grouped by their nearest "### Heading".
#
# Usage:
#   tools/plan-progress.sh [plan.md] [--json] [--strict]
#                          [--phases M0,M1,...] [--section "Heading text"]
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: tools/plan-progress.sh [plan.md] [options]

  --json                 Print machine-readable JSON instead of a table.
  --strict               Exit 1 when the selected scope has open or in-progress items.
  --phases M0,M1,...     Only count sections whose heading starts with one of these phases.
  --section "text"       Only count sections whose heading contains text (case-insensitive).
  -h, --help             Show this help.

Checklist states:
  - [ ]  open
  - [~]  in progress
  - [x]  done
EOF
}

die() {
  echo "error: $*" >&2
  exit 2
}

plan="docs/plan.md"
json=0
strict=0
phase_filter=""
section_filter=""

while [ $# -gt 0 ]; do
  case "$1" in
    --json) json=1; shift ;;
    --strict) strict=1; shift ;;
    --phases)
      [ $# -ge 2 ] || die "--phases needs a value"
      phase_filter=$2
      shift 2
      ;;
    --section)
      [ $# -ge 2 ] || die "--section needs a value"
      section_filter=$2
      shift 2
      ;;
    -h|--help) usage; exit 0 ;;
    -*)
      usage
      die "unknown option '$1'"
      ;;
    *)
      plan=$1
      shift
      ;;
  esac
done

if [ ! -f "$plan" ]; then
  if root=$(git rev-parse --show-toplevel 2>/dev/null); then
    [ -f "$root/$plan" ] && plan="$root/$plan"
  fi
fi
[ -f "$plan" ] || die "plan file not found: $plan"

phase_re=""
if [ -n "$phase_filter" ]; then
  old_ifs=$IFS
  IFS=','
  for raw_phase in $phase_filter; do
    phase=$(printf '%s' "$raw_phase" | tr -d '[:space:]')
    [ -n "$phase" ] || continue
    case "$phase" in
      M[0-9]|M[0-9][0-9]) ;;
      *) IFS=$old_ifs; die "invalid phase '$phase' (expected M0 style)" ;;
    esac
    if [ -z "$phase_re" ]; then
      phase_re="^$phase([^0-9]|$)"
    else
      phase_re="$phase_re|^$phase([^0-9]|$)"
    fi
  done
  IFS=$old_ifs
  [ -n "$phase_re" ] || die "--phases did not contain a valid phase"
fi

tmp=$(mktemp "${TMPDIR:-/tmp}/rok-plan-progress.XXXXXX")
trap 'rm -f "$tmp"' EXIT

awk -v phase_re="$phase_re" -v sec_filter="$section_filter" '
  function trim(s) {
    sub(/^[ \t]+/, "", s)
    sub(/[ \t]+$/, "", s)
    return s
  }
  BEGIN {
    section = "(top)"
    selected = (phase_re == "" && sec_filter == "") ? 1 : 0
    n = 0
    in_fence = 0
  }
  /^[ \t]*```/ {
    in_fence = !in_fence
    next
  }
  in_fence { next }
  /^###[ \t]+/ {
    section = trim(substr($0, 5))
    selected = 1
    if (phase_re != "" && section !~ phase_re) selected = 0
    if (sec_filter != "" && index(tolower(section), tolower(sec_filter)) == 0) selected = 0
    next
  }
  /^[ \t]*- \[[ xX~]\]/ {
    if (!selected) next
    if (!(section in seen)) {
      seen[section] = 1
      order[++n] = section
    }
    total[section]++
    total_all++
    if ($0 ~ /^[ \t]*- \[[xX]\]/) {
      done[section]++
      done_all++
    } else if ($0 ~ /^[ \t]*- \[~\]/) {
      wip[section]++
      wip_all++
    } else {
      open[section]++
      open_all++
    }
    next
  }
  END {
    if (total_all == 0) {
      print "TOTAL\t0\t0\t0\t0"
      exit 0
    }
    printf "TOTAL\t%d\t%d\t%d\t%d\n", done_all, wip_all, open_all, total_all
    for (i = 1; i <= n; i++) {
      s = order[i]
      printf "SECTION\t%s\t%d\t%d\t%d\t%d\n", s, done[s] + 0, wip[s] + 0, open[s] + 0, total[s] + 0
    }
  }
' "$plan" > "$tmp"

total_done=0
total_wip=0
total_open=0
total_all=0
sections=""

while IFS=$'\t' read -r kind a b c d e; do
  case "$kind" in
    TOTAL)
      total_done=$a
      total_wip=$b
      total_open=$c
      total_all=$d
      ;;
    SECTION)
      sections="${sections}${a}"$'\t'"${b}"$'\t'"${c}"$'\t'"${d}"$'\t'"${e}"$'\n'
      ;;
  esac
done < "$tmp"

percent() {
  local done=$1 total=$2
  if [ "$total" -le 0 ]; then
    printf '0'
  else
    printf '%d' $((done * 100 / total))
  fi
}

bar() {
  local done=$1 total=$2 width=${3:-24} filled empty
  if [ "$total" -le 0 ]; then
    filled=0
  else
    filled=$((done * width / total))
  fi
  empty=$((width - filled))
  printf '['
  if [ "$filled" -gt 0 ]; then
    printf '%*s' "$filled" '' | tr ' ' '#'
  fi
  if [ "$empty" -gt 0 ]; then
    printf '%*s' "$empty" '' | tr ' ' '-'
  fi
  printf ']'
}

json_escape() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

finish() {
  local code=$1
  if [ "$strict" -eq 1 ] && [ $((total_wip + total_open)) -gt 0 ]; then
    exit 1
  fi
  exit "$code"
}

if [ "$json" -eq 1 ]; then
  printf '{\n'
  printf '  "plan": "%s",\n' "$(json_escape "$plan")"
  if [ -n "$phase_filter" ] || [ -n "$section_filter" ]; then
    printf '  "scope": { "phases": "%s", "section": "%s" },\n' \
      "$(json_escape "$phase_filter")" "$(json_escape "$section_filter")"
  fi
  printf '  "overall": { "done": %d, "wip": %d, "open": %d, "total": %d, "percent": %d },\n' \
    "$total_done" "$total_wip" "$total_open" "$total_all" "$(percent "$total_done" "$total_all")"
  printf '  "sections": ['
  first=1
  while IFS=$'\t' read -r name done wip open total; do
    [ -n "$name" ] || continue
    if [ "$first" -eq 1 ]; then
      printf '\n'
      first=0
    else
      printf ',\n'
    fi
    printf '    { "name": "%s", "done": %d, "wip": %d, "open": %d, "total": %d, "percent": %d }' \
      "$(json_escape "$name")" "$done" "$wip" "$open" "$total" "$(percent "$done" "$total")"
  done <<EOF
$sections
EOF
  if [ "$first" -eq 0 ]; then printf '\n  '; fi
  printf ']\n}\n'
  finish 0
fi

echo "Rok Mahjong plan progress — $plan"
if [ -n "$phase_filter" ]; then echo "Scope: phases $phase_filter"; fi
if [ -n "$section_filter" ]; then echo "Scope: section containing \"$section_filter\""; fi
if [ "$total_all" -eq 0 ]; then
  echo "No checklist items found in this scope."
  finish 2
fi

printf 'Overall: %d/%d done, %d in progress, %d open  %s %d%%\n' \
  "$total_done" "$total_all" "$total_wip" "$total_open" \
  "$(bar "$total_done" "$total_all")" "$(percent "$total_done" "$total_all")"
echo

while IFS=$'\t' read -r name done wip open total; do
  [ -n "$name" ] || continue
  short=$(printf '%s' "$name" | cut -c1-54)
  printf '  %-54s %3d/%-3d  %s %3d%%  (%d wip, %d open)\n' \
    "$short" "$done" "$total" "$(bar "$done" "$total" 18)" "$(percent "$done" "$total")" "$wip" "$open"
done <<EOF
$sections
EOF

finish 0
