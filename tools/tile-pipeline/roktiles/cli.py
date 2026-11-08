"""`roktiles` command line interface."""

from __future__ import annotations

import argparse
import io
import random
import sys
import time
import uuid
from pathlib import Path

from PIL import Image

from . import applier, config, faces as face_catalog, store, sheet
from .faces import Face
from .prompts import DEFAULT_STYLE, STYLES, build_prompt
from .providers import PROVIDERS, GenerationRequest, create_provider


def _id(prefix: str) -> str:
    return f"{prefix}-{uuid.uuid4().hex[:8]}"


def _print_table(headers: list[str], rows: list[list[str]]) -> None:
    if not rows:
        print("  (nothing to show)")
        return
    widths = [len(header) for header in headers]
    for row in rows:
        for index, cell in enumerate(row):
            widths[index] = max(widths[index], len(cell))
    print("  " + "  ".join(header.ljust(widths[i]) for i, header in enumerate(headers)))
    print("  " + "  ".join("-" * width for width in widths))
    for row in rows:
        print("  " + "  ".join(cell.ljust(widths[i]) for i, cell in enumerate(row)))


def _select_faces(args: argparse.Namespace) -> list[Face]:
    return face_catalog.resolve_labels(
        getattr(args, "face", None),
        getattr(args, "group", None),
        getattr(args, "all", False),
    )


def cmd_faces(args: argparse.Namespace) -> int:
    selections = store.selection_generation_ids()
    rows = []
    for face in face_catalog.FACES:
        selected = selections.get(face.label, "-")
        rows.append([face.label, face.group, face.description, selected])
    _print_table(["LABEL", "GROUP", "SUBJECT", "SELECTED"], rows)
    print()
    print(f"  {len(face_catalog.FACES)} faces, {len(selections)} selected.")
    return 0


def cmd_styles(args: argparse.Namespace) -> int:
    rows = [[style.name, style.summary] for style in STYLES.values()]
    _print_table(["STYLE", "LOOK"], rows)
    print()
    print(f"  default: {DEFAULT_STYLE}")
    return 0


def cmd_prompt(args: argparse.Namespace) -> int:
    face = face_catalog.BY_LABEL[args.face]
    prompt, negative = build_prompt(face, args.style, args.extra or "")
    print(f"face:     {face.label} ({face.group})")
    print(f"reference: {config.REFERENCE_DIR / (face.label + '.png')}")
    print()
    print("prompt:")
    print(prompt)
    print()
    print("negative:")
    print(negative)
    return 0


def cmd_generate(args: argparse.Namespace) -> int:
    selected_faces = _select_faces(args)
    options = _provider_options(args)
    provider = create_provider(args.provider, args.model, options=options)
    settings = {
        "style": args.style,
        "steps": args.steps,
        "cfg": args.cfg,
        "denoise": args.denoise,
        "sampler": args.sampler,
        "scheduler": args.scheduler,
        "shift": args.shift,
        "lora": getattr(args, "lora", None) or config.COMFYUI_LORA or None,
        "lora_strength": getattr(args, "lora_strength", 1.0),
        "extra": args.extra or "",
    }
    run_id = _id("r")

    print(f"run {run_id}: {len(selected_faces)} face(s) via {provider.name} / {provider.model}")
    print(f"style: {args.style} — {STYLES[args.style].summary}")
    if args.dry_run:
        for face in selected_faces:
            prompt, negative = build_prompt(face, args.style, args.extra or "")
            print(f"\n[{face.label}] {face.description}\n  {prompt}")
        return 0

    store.create_run(run_id, provider.name, provider.model, args.style, settings)

    config.ensure_directories()
    run_dir = config.HISTORY_DIR / run_id
    run_dir.mkdir(parents=True, exist_ok=True)

    base_seed = args.seed
    failures = 0
    produced: list[store.Generation] = []
    run_started = time.monotonic()
    for index, face in enumerate(selected_faces, start=1):
        seed = (
            (base_seed + index - 1) % (2**31 - 1)
            if base_seed is not None
            else random.SystemRandom().randrange(1, 2**31 - 1)
        )
        reference = config.REFERENCE_DIR / f"{face.label}.png"
        if not reference.is_file():
            print(
                f"  ! {face.label}: missing reference art at {reference}; "
                "run `make tiles` first.",
                file=sys.stderr,
            )
            failures += 1
            if not args.keep_going:
                break
            continue

        prompt, negative = build_prompt(face, args.style, args.extra or "")
        started = time.monotonic()
        print(f"  [{index}/{len(selected_faces)}] {face.label} seed={seed} … ", end="", flush=True)
        try:
            request = GenerationRequest(
                reference=reference,
                prompt=prompt,
                negative_prompt=negative,
                seed=seed,
                width=args.width,
                height=args.height,
                options=options,
            )
            output = provider.generate(request)
        except Exception as error:  # noqa: BLE001 - keep a long batch alive
            elapsed = time.monotonic() - started
            print(f"failed ({elapsed:.1f}s)")
            print(f"    {type(error).__name__}: {error}", file=sys.stderr)
            failures += 1
            if not args.keep_going:
                break
            continue

        elapsed = time.monotonic() - started
        generation_id = _id("g")
        image_path = run_dir / f"{face.label}_{generation_id}.png"
        image_path.write_bytes(output.image)
        try:
            with Image.open(io.BytesIO(output.image)) as image:
                width, height = image.size
        except Exception:  # noqa: BLE001 - treat undecodable output as a failure
            image_path.unlink(missing_ok=True)
            print(f"failed (undecodable image, {elapsed:.1f}s)")
            failures += 1
            if not args.keep_going:
                break
            continue

        params = {
            "provider": provider.name,
            "model": provider.model,
            "style": args.style,
            **output.meta,
        }
        generation = store.add_generation(
            generation_id=generation_id,
            run_id=run_id,
            face=face.label,
            image_path=image_path,
            width=width,
            height=height,
            prompt=prompt,
            negative_prompt=negative,
            seed=seed,
            params=params,
            duration_ms=int(elapsed * 1000),
        )
        produced.append(generation)
        print(f"{generation_id} ({width}x{height}, {elapsed:.1f}s)")
        remaining = len(selected_faces) - index
        if remaining > 0:
            average = (time.monotonic() - run_started) / index
            print(
                f"    eta ~{average * remaining / 60:.0f} min for "
                f"{remaining} remaining face(s)"
            )

    if produced:
        print()
        print(f"  stored {len(produced)} generation(s) under {run_dir.relative_to(config.REPO_ROOT)}")
        print("  select one with: roktiles select <generation-id>")
    if failures:
        print(f"  {failures} failure(s).", file=sys.stderr)
        return 1
    return 0


def _provider_options(args: argparse.Namespace) -> dict:
    return {
        "steps": args.steps,
        "cfg": args.cfg,
        "denoise": args.denoise,
        "sampler": args.sampler,
        "scheduler": args.scheduler,
        "shift": args.shift,
        "width": args.width,
        "height": args.height,
        "lora": getattr(args, "lora", None) or config.COMFYUI_LORA or None,
        "lora_strength": getattr(args, "lora_strength", 1.0),
    }


def cmd_history(args: argparse.Namespace) -> int:
    generations = store.list_generations(args.face, args.run, args.limit)
    selections = store.selection_generation_ids()
    rows = []
    for generation in generations:
        marker = "*" if selections.get(generation.face) == generation.id else " "
        rows.append(
            [
                marker,
                generation.id,
                generation.face,
                generation.params.get("provider", "?"),
                generation.params.get("model", "?")[:28],
                generation.params.get("style", "-"),
                str(generation.seed),
                generation.created_at.replace("T", " ")[:16],
                "ok" if generation.exists() else "MISSING",
            ]
        )
    _print_table(
        ["", "GENERATION", "FACE", "PROVIDER", "MODEL", "STYLE", "SEED", "CREATED", "FILE"],
        rows,
    )
    print()
    print(f"  history: {config.HISTORY_DIR.relative_to(config.REPO_ROOT)}")
    print("  * = currently selected")
    return 0


def cmd_runs(args: argparse.Namespace) -> int:
    runs = store.list_runs(args.limit)
    counts = store.run_counts()
    rows = [
        [
            run.id,
            run.provider,
            run.model[:32],
            run.style,
            str(counts.get(run.id, 0)),
            run.created_at.replace("T", " ")[:16],
        ]
        for run in runs
    ]
    _print_table(["RUN", "PROVIDER", "MODEL", "STYLE", "FACES", "CREATED"], rows)
    return 0


def cmd_select(args: argparse.Namespace) -> int:
    chosen: list[store.Generation] = []
    for generation_id in args.ids:
        generation = store.get_generation(generation_id)
        if generation is None:
            print(f"unknown generation: {generation_id}", file=sys.stderr)
            return 1
        if not generation.exists():
            print(f"generation image is missing: {generation.image_path}", file=sys.stderr)
            return 1
        chosen.append(generation)

    for generation in chosen:
        store.set_selection(generation.face, generation.id)
        print(f"selected {generation.face} <- {generation.id} ({generation.params.get('style', '-')})")

    written = applier.apply_selections(
        [generation.face for generation in chosen], build=not args.no_build
    )
    if written:
        print(f"applied {len(written)} face(s) to {config.SELECTED_DIR.relative_to(config.REPO_ROOT)}")
    return 0


def cmd_select_run(args: argparse.Namespace) -> int:
    run = store.get_run(args.run_id)
    if run is None:
        print(f"unknown run: {args.run_id}", file=sys.stderr)
        return 1
    generations = store.list_generations(run_id=args.run_id)
    latest: dict[str, store.Generation] = {}
    for generation in generations:
        latest.setdefault(generation.face, generation)
    if not latest:
        print(f"run {args.run_id} has no generations", file=sys.stderr)
        return 1
    for label, generation in latest.items():
        if generation.exists():
            store.set_selection(label, generation.id)
    written = applier.apply_selections(list(latest), build=not args.no_build)
    print(f"selected {len(written)} face(s) from run {args.run_id}")
    return 0


def cmd_apply(args: argparse.Namespace) -> int:
    written = applier.apply_selections(build=not args.no_build)
    if not written:
        print("No selections yet. Use `roktiles select <generation-id>`.")
        return 0
    print(f"applied {len(written)} selected face(s); atlas rebuilt.")
    return 0


def cmd_reset(args: argparse.Namespace) -> int:
    if args.all:
        store.clear_selection()
        removed = applier.remove_faces(None, build=not args.no_build)
    else:
        if not args.face:
            print("choose --face LABEL (repeatable) or --all", file=sys.stderr)
            return 1
        for label in args.face:
            if label not in face_catalog.BY_LABEL:
                print(f"unknown face: {label}", file=sys.stderr)
                return 1
            store.clear_selection(label)
        removed = applier.remove_faces(args.face, build=not args.no_build)
    print(f"reset {len(removed)} face(s) to the vector fallback.")
    return 0


def cmd_sheet(args: argparse.Namespace) -> int:
    generations = store.list_generations(
        face=args.face, run_id=args.run, limit=args.limit
    )
    if args.latest_per_face:
        seen: set[str] = set()
        filtered: list[store.Generation] = []
        for generation in generations:
            if generation.face in seen:
                continue
            seen.add(generation.face)
            filtered.append(generation)
        generations = filtered
    if not generations:
        print("No generations match. Run `roktiles generate` first.")
        return 1
    output = Path(args.output) if args.output else sheet.default_output(args.name)
    path = sheet.build_sheet(generations, output, columns=args.columns)
    print(f"sheet: {path}")
    print(f"{len(generations)} generation(s); * marks the current selection")
    if args.open:
        import subprocess

        subprocess.run(["open", str(path)], check=False)
    return 0


def cmd_show(args: argparse.Namespace) -> int:
    generation = store.get_generation(args.id)
    if generation is None:
        print(f"unknown generation: {args.id}", file=sys.stderr)
        return 1
    print(generation.image_path)
    print(f"face: {generation.face}  style: {generation.params.get('style', '-')}  seed: {generation.seed}")
    print(f"prompt: {generation.prompt}")
    if args.open:
        import subprocess

        subprocess.run(["open", str(generation.image_path)], check=False)
    return 0


def cmd_status(args: argparse.Namespace) -> int:
    config.ensure_directories()
    generations = store.list_generations(limit=None)
    selections = store.selected_generations()
    references = list(config.REFERENCE_DIR.glob("*.png"))
    missing_references = [
        face.label
        for face in face_catalog.FACES
        if not (config.REFERENCE_DIR / f"{face.label}.png").is_file()
    ]
    print(f"repo:        {config.REPO_ROOT}")
    print(f"history db:  {config.DB_PATH} ({len(generations)} generations)")
    print(f"references:  {len(references)}/{len(face_catalog.FACES)}")
    if missing_references:
        print(f"  missing references: {', '.join(missing_references[:8])}… run `make tiles`")
    print(f"selected:    {len(selections)}/{len(face_catalog.FACES)}")
    print(f"comfyui:     {config.COMFYUI_URL}")
    print(f"openrouter:  {'key set' if config.OPENROUTER_API_KEY else 'no key (adapter disabled)'}")
    if selections:
        print()
        rows = [
            [
                label,
                generation.id,
                generation.params.get("style", "-"),
                generation.params.get("model", "-")[:34],
                generation.created_at.replace("T", " ")[:16],
            ]
            for label, generation in sorted(selections.items())
        ]
        _print_table(["FACE", "GENERATION", "STYLE", "MODEL", "CREATED"], rows)
    return 0


def cmd_doctor(args: argparse.Namespace) -> int:
    import shutil

    problems = 0
    references = sorted(path.stem for path in config.REFERENCE_DIR.glob("*.png"))
    expected = [face.label for face in face_catalog.FACES]
    missing = [label for label in expected if label not in references]
    print(f"[{'ok' if not missing else '!!'}] reference art: {len(references)}/{len(expected)}")
    if missing:
        problems += 1
        print(f"     missing: {', '.join(missing)} — run `make tiles`")

    print(f"[{'ok' if shutil.which('dotnet') else '!!'}] dotnet on PATH")
    if shutil.which("dotnet") is None:
        problems += 1

    if args.provider == "comfyui":
        provider = create_provider("comfyui", args.model, options=_provider_options(args))
        try:
            stats = provider.health()  # type: ignore[attr-defined]
            system = stats.get("system", {})
            print(f"[ok] ComfyUI reachable: version {system.get('comfyui_version', '?')}")
            devices = stats.get("devices") or []
            if devices:
                device = devices[0]
                print(f"     device: {device.get('name', '?')} ({device.get('type', '?')})")
            print(f"     unet: {provider.model}")
            print(f"     clip: {provider.clip}")
            print(f"     vae:  {provider.vae}")
            if config.COMFYUI_LORA:
                print(f"     lora: {config.COMFYUI_LORA}")
        except Exception as error:  # noqa: BLE001 - doctor reports, never crashes
            problems += 1
            print(f"[!!] ComfyUI unreachable: {error}")
    elif args.provider == "openrouter":
        if config.OPENROUTER_API_KEY:
            print(f"[ok] OpenRouter key set (model {config.OPENROUTER_MODEL})")
        else:
            print("[--] OpenRouter key not set; adapter disabled (optional)")

    print()
    print("ready" if problems == 0 else f"{problems} problem(s) found")
    return 0 if problems == 0 else 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="roktiles",
        description="Generate, keep and select Rok Mahjong tile face art.",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    faces_parser = subparsers.add_parser("faces", help="list the 42 faces")
    faces_parser.set_defaults(func=cmd_faces)

    styles_parser = subparsers.add_parser("styles", help="list style presets")
    styles_parser.set_defaults(func=cmd_styles)

    prompt_parser = subparsers.add_parser("prompt", help="print the prompt for a face")
    prompt_parser.add_argument("face")
    prompt_parser.add_argument("--style", default=DEFAULT_STYLE)
    prompt_parser.add_argument("--extra", default="")
    prompt_parser.set_defaults(func=cmd_prompt)

    generate_parser = subparsers.add_parser("generate", help="generate face art")
    generate_parser.add_argument("--face", action="append", help="face label (repeatable)")
    generate_parser.add_argument("--group", choices=face_catalog.GROUPS)
    generate_parser.add_argument("--all", action="store_true", help="all 42 faces")
    generate_parser.add_argument("--provider", default="comfyui", choices=PROVIDERS)
    generate_parser.add_argument("--model", default=None, help="provider model id")
    generate_parser.add_argument("--style", default=DEFAULT_STYLE, choices=sorted(STYLES))
    generate_parser.add_argument("--seed", type=int, default=None, help="base seed")
    generate_parser.add_argument("--steps", type=int, default=10)
    generate_parser.add_argument("--cfg", type=float, default=4.0)
    generate_parser.add_argument("--denoise", type=float, default=1.0)
    generate_parser.add_argument("--sampler", default="euler")
    generate_parser.add_argument("--scheduler", default="simple")
    generate_parser.add_argument("--shift", type=float, default=3.1)
    generate_parser.add_argument(
        "--lora", default=None, help="ComfyUI LoRA filename (e.g. the 4-step Lightning LoRA)"
    )
    generate_parser.add_argument("--lora-strength", type=float, default=1.0)
    generate_parser.add_argument("--width", type=int, default=832)
    generate_parser.add_argument("--height", type=int, default=1152)
    generate_parser.add_argument("--extra", default="", help="extra prompt text")
    generate_parser.add_argument("--keep-going", action="store_true")
    generate_parser.add_argument("--dry-run", action="store_true")
    generate_parser.set_defaults(func=cmd_generate)

    history_parser = subparsers.add_parser("history", help="list generations")
    history_parser.add_argument("--face")
    history_parser.add_argument("--run")
    history_parser.add_argument("--limit", type=int, default=30)
    history_parser.set_defaults(func=cmd_history)

    runs_parser = subparsers.add_parser("runs", help="list generation runs")
    runs_parser.add_argument("--limit", type=int, default=20)
    runs_parser.set_defaults(func=cmd_runs)

    select_parser = subparsers.add_parser("select", help="select generation(s) and apply")
    select_parser.add_argument("ids", nargs="+", help="generation id(s)")
    select_parser.add_argument("--no-build", action="store_true", help="skip atlas rebuild")
    select_parser.set_defaults(func=cmd_select)

    select_run_parser = subparsers.add_parser(
        "select-run", help="select every face from one run"
    )
    select_run_parser.add_argument("run_id")
    select_run_parser.add_argument("--no-build", action="store_true")
    select_run_parser.set_defaults(func=cmd_select_run)

    apply_parser = subparsers.add_parser("apply", help="re-apply current selections")
    apply_parser.add_argument("--no-build", action="store_true")
    apply_parser.set_defaults(func=cmd_apply)

    reset_parser = subparsers.add_parser("reset", help="drop selections back to vector art")
    reset_parser.add_argument("--face", action="append")
    reset_parser.add_argument("--all", action="store_true")
    reset_parser.add_argument("--no-build", action="store_true")
    reset_parser.set_defaults(func=cmd_reset)

    status_parser = subparsers.add_parser("status", help="pipeline overview")
    status_parser.set_defaults(func=cmd_status)

    sheet_parser = subparsers.add_parser(
        "sheet", help="render a contact sheet of generations"
    )
    sheet_parser.add_argument("--face", help="only this face")
    sheet_parser.add_argument("--run", help="only this run")
    sheet_parser.add_argument("--limit", type=int, default=60)
    sheet_parser.add_argument(
        "--latest-per-face",
        action="store_true",
        help="one row per face (the newest generation)",
    )
    sheet_parser.add_argument("--columns", type=int, default=6)
    sheet_parser.add_argument("--name", default="sheet")
    sheet_parser.add_argument("--output", default=None)
    sheet_parser.add_argument("--open", action="store_true")
    sheet_parser.set_defaults(func=cmd_sheet)

    show_parser = subparsers.add_parser("show", help="print a generation's path/prompt")
    show_parser.add_argument("id")
    show_parser.add_argument("--open", action="store_true")
    show_parser.set_defaults(func=cmd_show)

    doctor_parser = subparsers.add_parser("doctor", help="check prerequisites")
    doctor_parser.add_argument("--provider", default="comfyui", choices=PROVIDERS)
    doctor_parser.add_argument("--model", default=None)
    doctor_parser.add_argument("--steps", type=int, default=20)
    doctor_parser.add_argument("--cfg", type=float, default=4.0)
    doctor_parser.add_argument("--denoise", type=float, default=1.0)
    doctor_parser.add_argument("--sampler", default="euler")
    doctor_parser.add_argument("--scheduler", default="simple")
    doctor_parser.add_argument("--shift", type=float, default=3.1)
    doctor_parser.add_argument("--lora", default=None)
    doctor_parser.add_argument("--lora-strength", type=float, default=1.0)
    doctor_parser.add_argument("--width", type=int, default=832)
    doctor_parser.add_argument("--height", type=int, default=1152)
    doctor_parser.set_defaults(func=cmd_doctor)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except (ValueError, RuntimeError, FileNotFoundError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(main())
