#!/usr/bin/env bash
# Push a submodule feature branch and open its upstream pull request.
#
# Usage:
#   ./scripts/submodule-finish.sh <monogame|forma> <branch>
#
# The branch must already exist inside the submodule and its worktree must be
# clean. The script pushes to "origin" and uses the GitHub CLI when available.
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: ./scripts/submodule-finish.sh <monogame|forma> <branch>
EOF
}

die() {
  echo "error: $*" >&2
  exit 1
}

[ $# -eq 2 ] || { usage; exit 2; }

name=$1
branch=$2

case "$name" in
  monogame)
    path="third_party/MonoGame"
    upstream="zigrok/MonoGame"
    base_branch="develop"
    ;;
  forma)
    path="third_party/Forma"
    upstream="zigrok/Forma"
    base_branch="main"
    ;;
  *)
    usage
    die "unknown submodule '$name'"
    ;;
esac

root=$(git rev-parse --show-toplevel 2>/dev/null) || die "not inside a Git repository"
cd "$root"

[ -e "$path/.git" ] || die "$path is not initialized"
git -C "$path" show-ref --verify --quiet "refs/heads/$branch" \
  || die "branch '$branch' does not exist in $path"

current=$(git -C "$path" branch --show-current)
[ "$current" = "$branch" ] || die "$path is on '$current', not '$branch'"

if [ -n "$(git -C "$path" status --porcelain)" ]; then
  git -C "$path" status --short
  die "$path has uncommitted changes. Commit or stash them first."
fi

sha=$(git -C "$path" rev-parse HEAD)
short_sha=${sha:0:12}

echo "Pushing $sha to origin/$branch ..."
git -C "$path" push -u origin "$branch"

if command -v gh >/dev/null 2>&1; then
  if gh pr view "$branch" --repo "$upstream" >/dev/null 2>&1; then
    echo "A pull request for '$branch' already exists:"
    gh pr view "$branch" --repo "$upstream" --json url,state --jq '"\(.state) \(.url)"'
  else
    echo "Opening a pull request against $upstream:$base_branch ..."
    gh pr create --repo "$upstream" --base "$base_branch" --head "$branch" --fill
  fi
else
  echo "GitHub CLI not found. Open a PR manually:"
  echo "  https://github.com/$upstream/compare/$base_branch...$branch?expand=1"
fi

cat <<EOF

After the upstream PR is merged:
  git -C $path fetch origin
  git -C $path checkout <merged-sha>
  git add $path
  git commit -m "chore(deps): bump ${name} to <short-sha>"
  ./scripts/check-submodules.sh

Do not merge the game PR while this branch is only pinned at an unmerged
feature commit.
EOF
