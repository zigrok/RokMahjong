#!/usr/bin/env bash
# Quick completion gate for the /go desktop goal.
#
# This script only re-reads evidence the agent already produced. It does not run
# the build, test suite, gameplay suite or gallery again; those are recorded in
# artifacts/goal/*.log by the agent.
set -euo pipefail

root=$(git rev-parse --show-toplevel 2>/dev/null) || {
  echo "error: not inside a Git repository" >&2
  exit 2
}
cd "$root"

fail=0

pass() { echo "PASS  $*"; }
bad()  { echo "FAIL  $*"; fail=1; }

need_file() {
  local path=$1
  if [ -f "$path" ]; then
    pass "$path exists"
  else
    bad "$path is missing"
  fi
}

contains() {
  local path=$1 text=$2 label=$3
  if [ -f "$path" ] && grep -qF -- "$text" "$path"; then
    pass "$label"
  else
    bad "$label ($path must contain '$text')"
  fi
}

not_contains() {
  local path=$1 text=$2 label=$3
  if [ -f "$path" ] && grep -qF -- "$text" "$path"; then
    bad "$label ($path contains '$text')"
  else
    pass "$label"
  fi
}

echo "== Plan checklist: desktop phases =="
if ./tools/plan-progress.sh docs/plan.md --phases M0,M1,M2,M3,M4,M5 --strict; then
  pass "M0-M5 checklist is complete"
else
  bad "M0-M5 checklist still has open or in-progress items"
fi

echo
echo "== Plan checklist: Desktop v1 acceptance =="
if ./tools/plan-progress.sh docs/plan.md --section "Desktop v1 acceptance" --strict; then
  pass "Desktop v1 acceptance checklist is complete"
else
  bad "Desktop v1 acceptance checklist still has open or in-progress items"
fi

echo
echo "== Evidence logs =="
contains "artifacts/goal/desktop-build.log" "0 Error(s)" "release build has 0 errors"
contains "artifacts/goal/desktop-build.log" "0 Warning(s)" "release build has 0 warnings"
contains "artifacts/goal/tests.log" "Passed!" "test suite reported passed"
not_contains "artifacts/goal/tests.log" "Failed!" "test suite did not report failures"
contains "artifacts/goal/gameplay.log" "GOAL GAMEPLAY OK" "headless gameplay suite marked successful"
contains "artifacts/goal/gallery.log" "GOAL GALLERY OK" "visual gallery marked successful"
contains "artifacts/goal/desktop-ready.md" "Status: READY" "desktop-ready summary is marked ready"

echo
echo "== Repository state =="
if [ -z "$(git status --porcelain)" ]; then
  pass "working tree is clean"
else
  bad "working tree has uncommitted changes"
  git status --short
fi

if ./scripts/check-submodules.sh; then
  pass "submodule pins are reachable"
else
  bad "submodule pin check failed"
fi

echo
if [ "$fail" -eq 0 ]; then
  echo "GOAL GATE OK"
  exit 0
fi

echo "GOAL GATE FAILED"
echo "Run the missing proof, append GOAL GAMEPLAY OK / GOAL GALLERY OK after a"
echo "successful run, write artifacts/goal/desktop-ready.md, and commit the work."
exit 1
