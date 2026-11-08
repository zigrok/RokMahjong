#!/usr/bin/env bash
# Create a feature branch inside a pinned submodule.
#
# Usage:
#   ./scripts/submodule-start.sh <monogame|forma> <branch> [base-ref]
#
# Examples:
#   ./scripts/submodule-start.sh forma feat/mobile-safe-area
#   ./scripts/submodule-start.sh monogame fix/headless-present origin/develop
#
# The script only creates the branch. It never commits or pushes.
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: ./scripts/submodule-start.sh <monogame|forma> <branch> [base-ref]

  monogame   third_party/MonoGame, default base origin/develop
  forma      third_party/Forma,   default base origin/main

Branch names must look like: feat/<slug>, fix/<slug>, chore/<slug>,
docs/<slug>, test/<slug> or ci/<slug>.
EOF
}

die() {
  echo "error: $*" >&2
  exit 1
}

[ $# -ge 2 ] || { usage; exit 2; }

name=$1
branch=$2
base=${3:-}

case "$name" in
  monogame)
    path="third_party/MonoGame"
    default_base="origin/develop"
    upstream="zigrok/MonoGame"
    base_branch="develop"
    ;;
  forma)
    path="third_party/Forma"
    default_base="origin/main"
    upstream="zigrok/Forma"
    base_branch="main"
    ;;
  *)
    usage
    die "unknown submodule '$name'"
    ;;
esac

[[ "$branch" =~ ^(feat|fix|chore|docs|test|ci)/[a-z0-9][a-z0-9._-]*$ ]] \
  || die "branch '$branch' must match <type>/<slug> with type feat|fix|chore|docs|test|ci"

root=$(git rev-parse --show-toplevel 2>/dev/null) || die "not inside a Git repository"
cd "$root"

[ -e "$path/.git" ] || die "$path is not initialized. Run: git submodule update --init $path"

base=${base:-$default_base}
git -C "$path" fetch origin --prune

git -C "$path" rev-parse --verify --quiet "$base" >/dev/null \
  || die "'$base' does not exist in $path. Fetch origin and use a valid base ref."

if git -C "$path" show-ref --verify --quiet "refs/heads/$branch"; then
  die "branch '$branch' already exists in $path"
fi

git -C "$path" switch -c "$branch" "$base"

short_sha=$(git -C "$path" rev-parse --short HEAD)
cat <<EOF

Created branch '$branch' in $path from $base ($short_sha).

Next steps:
  cd $path
  # make and commit your changes
  git push -u origin $branch
  gh pr create --repo $upstream --base $base_branch --head $branch --fill

Or run the finish helper after committing:
  ./scripts/submodule-finish.sh $name $branch

Remember:
  - push the branch before pinning it in the game repository;
  - wait for upstream CI to pass;
  - after merge, update the submodule pointer to the merged commit.
EOF
