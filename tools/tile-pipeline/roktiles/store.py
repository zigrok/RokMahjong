"""SQLite-backed generation history and selection state."""

from __future__ import annotations

import json
import sqlite3
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

from . import config

SCHEMA = """
CREATE TABLE IF NOT EXISTS runs (
    id            TEXT PRIMARY KEY,
    created_at    TEXT NOT NULL,
    provider      TEXT NOT NULL,
    model         TEXT NOT NULL,
    style         TEXT NOT NULL,
    settings_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS generations (
    id              TEXT PRIMARY KEY,
    run_id          TEXT NOT NULL REFERENCES runs(id),
    face            TEXT NOT NULL,
    image_path      TEXT NOT NULL,
    width           INTEGER NOT NULL,
    height          INTEGER NOT NULL,
    prompt          TEXT NOT NULL,
    negative_prompt TEXT NOT NULL,
    seed            INTEGER NOT NULL,
    params_json     TEXT NOT NULL,
    created_at      TEXT NOT NULL,
    duration_ms     INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS generations_face_idx ON generations(face, created_at DESC);
CREATE INDEX IF NOT EXISTS generations_run_idx ON generations(run_id);

CREATE TABLE IF NOT EXISTS selections (
    face          TEXT PRIMARY KEY,
    generation_id TEXT NOT NULL REFERENCES generations(id),
    selected_at   TEXT NOT NULL
);
"""


def now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


@dataclass(frozen=True)
class Run:
    id: str
    created_at: str
    provider: str
    model: str
    style: str
    settings: dict


@dataclass(frozen=True)
class Generation:
    id: str
    run_id: str
    face: str
    image_path: Path
    width: int
    height: int
    prompt: str
    negative_prompt: str
    seed: int
    params: dict
    created_at: str
    duration_ms: int

    def exists(self) -> bool:
        return self.image_path.is_file()


def connect() -> sqlite3.Connection:
    config.DB_PATH.parent.mkdir(parents=True, exist_ok=True)
    connection = sqlite3.connect(config.DB_PATH)
    connection.row_factory = sqlite3.Row
    connection.executescript(SCHEMA)
    return connection


def _run_from_row(row: sqlite3.Row) -> Run:
    return Run(
        id=row["id"],
        created_at=row["created_at"],
        provider=row["provider"],
        model=row["model"],
        style=row["style"],
        settings=json.loads(row["settings_json"]),
    )


def _generation_from_row(row: sqlite3.Row) -> Generation:
    return Generation(
        id=row["id"],
        run_id=row["run_id"],
        face=row["face"],
        image_path=Path(row["image_path"]),
        width=row["width"],
        height=row["height"],
        prompt=row["prompt"],
        negative_prompt=row["negative_prompt"],
        seed=row["seed"],
        params=json.loads(row["params_json"]),
        created_at=row["created_at"],
        duration_ms=row["duration_ms"],
    )


def create_run(
    run_id: str,
    provider: str,
    model: str,
    style: str,
    settings: dict,
) -> Run:
    created = now()
    with connect() as connection:
        connection.execute(
            "INSERT INTO runs (id, created_at, provider, model, style, settings_json) "
            "VALUES (?, ?, ?, ?, ?, ?)",
            (run_id, created, provider, model, style, json.dumps(settings, sort_keys=True)),
        )
    return Run(run_id, created, provider, model, style, settings)


def get_run(run_id: str) -> Run | None:
    with connect() as connection:
        row = connection.execute("SELECT * FROM runs WHERE id = ?", (run_id,)).fetchone()
    return _run_from_row(row) if row else None


def list_runs(limit: int = 20) -> list[Run]:
    with connect() as connection:
        rows = connection.execute(
            "SELECT * FROM runs ORDER BY created_at DESC LIMIT ?", (limit,)
        ).fetchall()
    return [_run_from_row(row) for row in rows]


def add_generation(
    *,
    generation_id: str,
    run_id: str,
    face: str,
    image_path: Path,
    width: int,
    height: int,
    prompt: str,
    negative_prompt: str,
    seed: int,
    params: dict,
    duration_ms: int = 0,
) -> Generation:
    created = now()
    with connect() as connection:
        connection.execute(
            "INSERT INTO generations (id, run_id, face, image_path, width, height, prompt, "
            "negative_prompt, seed, params_json, created_at, duration_ms) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            (
                generation_id,
                run_id,
                face,
                str(image_path),
                width,
                height,
                prompt,
                negative_prompt,
                seed,
                json.dumps(params, sort_keys=True),
                created,
                duration_ms,
            ),
        )
    return Generation(
        generation_id,
        run_id,
        face,
        image_path,
        width,
        height,
        prompt,
        negative_prompt,
        seed,
        params,
        created,
        duration_ms,
    )


def get_generation(generation_id: str) -> Generation | None:
    with connect() as connection:
        row = connection.execute(
            "SELECT * FROM generations WHERE id = ?", (generation_id,)
        ).fetchone()
    return _generation_from_row(row) if row else None


def list_generations(
    face: str | None = None,
    run_id: str | None = None,
    limit: int | None = None,
) -> list[Generation]:
    query = "SELECT * FROM generations"
    clauses: list[str] = []
    args: list[object] = []
    if face:
        clauses.append("face = ?")
        args.append(face)
    if run_id:
        clauses.append("run_id = ?")
        args.append(run_id)
    if clauses:
        query += " WHERE " + " AND ".join(clauses)
    query += " ORDER BY created_at DESC, rowid DESC"
    if limit:
        query += " LIMIT ?"
        args.append(limit)
    with connect() as connection:
        rows = connection.execute(query, args).fetchall()
    return [_generation_from_row(row) for row in rows]


def set_selection(face: str, generation_id: str) -> None:
    with connect() as connection:
        connection.execute(
            "INSERT INTO selections (face, generation_id, selected_at) VALUES (?, ?, ?) "
            "ON CONFLICT(face) DO UPDATE SET generation_id = excluded.generation_id, "
            "selected_at = excluded.selected_at",
            (face, generation_id, now()),
        )


def clear_selection(face: str | None = None) -> None:
    with connect() as connection:
        if face:
            connection.execute("DELETE FROM selections WHERE face = ?", (face,))
        else:
            connection.execute("DELETE FROM selections")


def selected_generations() -> dict[str, Generation]:
    with connect() as connection:
        rows = connection.execute(
            "SELECT g.* FROM selections s JOIN generations g ON g.id = s.generation_id"
        ).fetchall()
    return {row["face"]: _generation_from_row(row) for row in rows}


def selection_generation_ids() -> dict[str, str]:
    with connect() as connection:
        rows = connection.execute(
            "SELECT face, generation_id FROM selections"
        ).fetchall()
    return {row["face"]: row["generation_id"] for row in rows}


def run_counts() -> dict[str, int]:
    with connect() as connection:
        rows = connection.execute(
            "SELECT run_id, COUNT(*) AS count FROM generations GROUP BY run_id"
        ).fetchall()
    return {row["run_id"]: row["count"] for row in rows}
