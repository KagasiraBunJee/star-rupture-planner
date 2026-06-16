from __future__ import annotations

import sqlite3
from contextlib import contextmanager
from pathlib import Path
from typing import Iterator


SCHEMA = """
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS items (
    item_id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT,
    category TEXT NOT NULL,
    stack INTEGER,
    level INTEGER,
    source_url TEXT NOT NULL,
    image_source_url TEXT,
    image_path TEXT,
    image_url TEXT
);

CREATE TABLE IF NOT EXISTS buildings (
    building_id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    family_name TEXT,
    tier INTEGER,
    category TEXT NOT NULL,
    description TEXT,
    power REAL,
    temperature REAL,
    source_url TEXT NOT NULL,
    image_source_url TEXT,
    image_path TEXT,
    image_url TEXT
);

CREATE TABLE IF NOT EXISTS recipes (
    recipe_key TEXT PRIMARY KEY,
    recipe_id TEXT NOT NULL,
    building_id TEXT NOT NULL REFERENCES buildings(building_id) ON DELETE CASCADE,
    recipe_level INTEGER,
    duration_seconds REAL NOT NULL,
    output_item_id TEXT NOT NULL REFERENCES items(item_id) ON DELETE CASCADE,
    output_quantity REAL NOT NULL,
    output_per_minute REAL NOT NULL,
    original_rate_text TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS recipe_inputs (
    recipe_key TEXT NOT NULL REFERENCES recipes(recipe_key) ON DELETE CASCADE,
    input_item_id TEXT NOT NULL,
    input_quantity REAL NOT NULL,
    input_per_minute REAL NOT NULL,
    PRIMARY KEY (recipe_key, input_item_id)
);

CREATE TABLE IF NOT EXISTS recipe_unlock_requirements (
    recipe_key TEXT NOT NULL REFERENCES recipes(recipe_key) ON DELETE CASCADE,
    item_id TEXT NOT NULL,
    required_quantity REAL NOT NULL,
    PRIMARY KEY (recipe_key, item_id)
);

CREATE TABLE IF NOT EXISTS item_unlock_usages (
    item_id TEXT NOT NULL,
    recipe_key TEXT NOT NULL REFERENCES recipes(recipe_key) ON DELETE CASCADE,
    required_quantity REAL NOT NULL,
    PRIMARY KEY (item_id, recipe_key)
);

CREATE TABLE IF NOT EXISTS item_usages (
    item_id TEXT NOT NULL,
    recipe_key TEXT NOT NULL REFERENCES recipes(recipe_key) ON DELETE CASCADE,
    consumed_quantity_per_cycle REAL NOT NULL,
    consumed_per_minute REAL NOT NULL,
    PRIMARY KEY (item_id, recipe_key)
);

CREATE TABLE IF NOT EXISTS corporations (
    corporation_id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT,
    source_url TEXT NOT NULL,
    icon_url TEXT,
    colour TEXT,
    max_level INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS corporation_levels (
    corporation_id TEXT NOT NULL REFERENCES corporations(corporation_id) ON DELETE CASCADE,
    level INTEGER NOT NULL,
    reputation INTEGER,
    PRIMARY KEY (corporation_id, level)
);

CREATE TABLE IF NOT EXISTS corporation_building_rewards (
    corporation_id TEXT NOT NULL REFERENCES corporations(corporation_id) ON DELETE CASCADE,
    level INTEGER NOT NULL,
    building_id TEXT NOT NULL,
    name TEXT,
    category TEXT,
    icon_url TEXT,
    PRIMARY KEY (corporation_id, level, building_id)
);

CREATE TABLE IF NOT EXISTS corporation_item_rewards (
    corporation_id TEXT NOT NULL REFERENCES corporations(corporation_id) ON DELETE CASCADE,
    level INTEGER NOT NULL,
    item_id TEXT NOT NULL,
    name TEXT,
    category TEXT,
    icon_url TEXT,
    PRIMARY KEY (corporation_id, level, item_id)
);

"""


def connect(db_path: Path) -> sqlite3.Connection:
    db_path.parent.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(db_path)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA foreign_keys = ON")
    return conn


@contextmanager
def connection(db_path: Path) -> Iterator[sqlite3.Connection]:
    conn = connect(db_path)
    try:
        yield conn
        conn.commit()
    finally:
        conn.close()


def init_db(conn: sqlite3.Connection) -> None:
    conn.executescript(SCHEMA)
    ensure_column(conn, "buildings", "power", "REAL")
    ensure_column(conn, "buildings", "temperature", "REAL")


def ensure_column(conn: sqlite3.Connection, table: str, column: str, definition: str) -> None:
    columns = {
        row["name"] if isinstance(row, sqlite3.Row) else row[1]
        for row in conn.execute(f"PRAGMA table_info({table})")
    }
    if column not in columns:
        conn.execute(f"ALTER TABLE {table} ADD COLUMN {column} {definition}")

