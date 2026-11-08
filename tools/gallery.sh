#!/usr/bin/env bash
# Render deterministic desktop screenshots and verify they contain real pixels.
set -euo pipefail
cd "$(dirname "$0")/.."

out="artifacts/gallery"
mkdir -p "$out"
dotnet build RokMahjong.slnx -v:minimal >/dev/null

capture() {
  local screen=$1
  local file=$2
  local level=${3:-}
  if [ -n "$level" ]; then
    dotnet run --project src/RokMahjong.Desktop --no-build -- --screenshot "$out/$file" --screen "$screen" --level "$level" >/dev/null
  else
    dotnet run --project src/RokMahjong.Desktop --no-build -- --screenshot "$out/$file" --screen "$screen" >/dev/null
  fi
}

capture menu menu.png
capture levels levels.png
capture game game.png
capture game game-144.png 9
capture settings settings.png
capture howto howto.png

python3 - <<'PY'
from PIL import Image
from pathlib import Path
out = Path('artifacts/gallery')
for name in ['menu', 'levels', 'game', 'game-144', 'settings', 'howto']:
    path = out / f'{name}.png'
    image = Image.open(path).convert('RGB')
    assert image.size[0] >= 1280 and image.size[1] >= 800, (path, image.size)
    assert image.size[0] * 800 == image.size[1] * 1280, (path, 'unexpected aspect', image.size)
    colors = image.getcolors(maxcolors=1 << 24)
    assert colors is not None and len(colors) > 24, (path, 'too uniform')
    print(f'{path}: {image.size[0]}x{image.size[1]}, {len(colors)} colors')
print('GOAL GALLERY OK')
PY
