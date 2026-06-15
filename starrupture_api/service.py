from __future__ import annotations

import json
import sqlite3
from typing import Any

from .config import Settings, settings
from .db import connection, init_db
from .importer import Importer
from .utils import PLACEHOLDER_RESOURCE_IDS, clean_number


class DataNotFoundError(KeyError):
    pass


class ResourceService:
    def __init__(self, cfg: Settings = settings) -> None:
        self.cfg = cfg
        with connection(self.cfg.db_path) as conn:
            init_db(conn)

    def refresh_dataset(self) -> dict[str, Any]:
        summary = Importer(self.cfg).refresh(download_images=True)
        return {
            "run_id": summary.run_id,
            "status": summary.status,
            "started_at": summary.started_at,
            "finished_at": summary.finished_at,
            "item_count": summary.item_count,
            "building_count": summary.building_count,
            "recipe_count": summary.recipe_count,
            "warnings": summary.warnings,
        }

    def get_meta(self) -> dict[str, Any]:
        with connection(self.cfg.db_path) as conn:
            counts = {
                "items": conn.execute(
                    self._clean_resource_count_sql()
                ).fetchone()[0],
                "related_items": conn.execute("SELECT COUNT(*) FROM items").fetchone()[0],
                "buildings": conn.execute("SELECT COUNT(*) FROM buildings").fetchone()[0],
                "recipes": conn.execute("SELECT COUNT(*) FROM recipes").fetchone()[0],
            }
            run = conn.execute(
                "SELECT * FROM refresh_runs ORDER BY run_id DESC LIMIT 1"
            ).fetchone()
        return {
            "dataset": "starrupture_resources",
            "source": self.cfg.base_url,
            "counts": counts,
            "last_refresh": self._refresh_run_payload(run) if run else None,
        }

    def list_items(
        self,
        *,
        q: str | None = None,
        produced: bool | None = None,
        used: bool | None = None,
        limit: int = 100,
        offset: int = 0,
    ) -> dict[str, Any]:
        clauses = [self._clean_resource_where("i")]
        params: list[Any] = []
        if q:
            clauses.append("(LOWER(i.item_id) LIKE ? OR LOWER(i.name) LIKE ?)")
            needle = f"%{q.lower()}%"
            params.extend([needle, needle])
        if produced is not None:
            op = "EXISTS" if produced else "NOT EXISTS"
            clauses.append(
                f"{op} (SELECT 1 FROM recipes r WHERE r.output_item_id = i.item_id)"
            )
        if used is not None:
            op = "EXISTS" if used else "NOT EXISTS"
            clauses.append(
                f"{op} (SELECT 1 FROM item_usages u WHERE u.item_id = i.item_id)"
            )
        where = " AND ".join(clauses)
        limit = min(max(limit, 1), 500)
        offset = max(offset, 0)
        with connection(self.cfg.db_path) as conn:
            total = conn.execute(
                f"SELECT COUNT(*) FROM items i WHERE {where}", params
            ).fetchone()[0]
            rows = conn.execute(
                f"""
                SELECT i.*,
                    EXISTS(SELECT 1 FROM recipes r WHERE r.output_item_id = i.item_id) AS produced,
                    EXISTS(SELECT 1 FROM item_usages u WHERE u.item_id = i.item_id) AS used,
                    EXISTS(
                        SELECT 1
                        FROM recipes r
                        JOIN recipe_unlock_requirements rr ON rr.recipe_key = r.recipe_key
                        WHERE r.output_item_id = i.item_id
                    ) AS requires_unlock,
                    EXISTS(
                        SELECT 1
                        FROM item_unlock_usages uu
                        WHERE uu.item_id = i.item_id
                    ) AS used_to_unlock
                FROM items i
                WHERE {where}
                ORDER BY i.name COLLATE NOCASE
                LIMIT ? OFFSET ?
                """,
                [*params, limit, offset],
            ).fetchall()
        return {
            "items": [self._item_summary(row) for row in rows],
            "total": total,
            "limit": limit,
            "offset": offset,
        }

    def _clean_resource_where(self, alias: str = "items") -> str:
        excluded = ", ".join(f"'{item_id}'" for item_id in sorted(PLACEHOLDER_RESOURCE_IDS))
        return f"{alias}.category = 'resource' AND {alias}.item_id NOT IN ({excluded})"

    def _clean_resource_count_sql(self) -> str:
        return f"SELECT COUNT(*) FROM items WHERE {self._clean_resource_where('items')}"

    def search_items(self, query: str, limit: int = 20) -> dict[str, Any]:
        return self.list_items(q=query, limit=limit, offset=0)

    def list_buildings(self) -> dict[str, Any]:
        with connection(self.cfg.db_path) as conn:
            rows = conn.execute(
                """
                SELECT b.*,
                    COUNT(r.recipe_key) AS recipe_count
                FROM buildings b
                LEFT JOIN recipes r ON r.building_id = b.building_id
                GROUP BY b.building_id
                ORDER BY b.name COLLATE NOCASE, b.building_id
                """
            ).fetchall()
        return {"buildings": [self._building_payload(row) for row in rows]}

    def get_transport_tiers(self) -> dict[str, Any]:
        if not self.cfg.transport_tiers_path.exists():
            return {
                "tiers": [],
                "missing": True,
                "message": "Transport tier speeds are not configured.",
            }
        with self.cfg.transport_tiers_path.open("r", encoding="utf-8") as handle:
            payload = json.load(handle)
        tiers = payload.get("tiers", []) if isinstance(payload, dict) else []
        return {
            "tiers": tiers,
            "missing": len(tiers) == 0,
            "message": payload.get("message") if isinstance(payload, dict) else None,
        }

    def get_corporations(self) -> dict[str, Any]:
        with connection(self.cfg.db_path) as conn:
            corporations = self._corporations_payload(conn)
        return {"corporations": corporations}

    def get_corporation_detail(self, corporation_id: str) -> dict[str, Any]:
        with connection(self.cfg.db_path) as conn:
            corporations = self._corporations_payload(conn)
        for corporation in corporations:
            if corporation["corporation_id"] == corporation_id:
                return corporation
        raise DataNotFoundError(corporation_id)

    def get_planner_catalog(self) -> dict[str, Any]:
        with connection(self.cfg.db_path) as conn:
            building_rows = conn.execute(
                """
                SELECT b.*,
                    COUNT(r.recipe_key) AS recipe_count
                FROM buildings b
                LEFT JOIN recipes r ON r.building_id = b.building_id
                GROUP BY b.building_id
                ORDER BY b.name COLLATE NOCASE, b.building_id
                """
            ).fetchall()
            recipe_rows = conn.execute(
                """
                SELECT r.*, b.name AS building_name, b.category AS building_category,
                       b.family_name AS building_family_name, b.tier AS building_tier,
                       b.image_url AS building_image_url,
                       oi.name AS output_item_name, oi.image_url AS output_item_image_url
                FROM recipes r
                JOIN buildings b ON b.building_id = r.building_id
                JOIN items oi ON oi.item_id = r.output_item_id
                ORDER BY b.name COLLATE NOCASE, r.recipe_level, r.recipe_id
                """
            ).fetchall()
            recipes = [self._planner_recipe_payload(conn, row) for row in recipe_rows]
            corporations = self._corporations_payload(conn)
            building_unlocks = self._building_unlocks_payload(conn)
        return {
            "buildings": [self._building_payload(row) for row in building_rows],
            "recipes": recipes,
            "transport_tiers": self.get_transport_tiers(),
            "corporations": corporations,
            "building_unlocks": building_unlocks,
            "meta": {
                "building_count": len(building_rows),
                "recipe_count": len(recipes),
            },
        }

    def get_planner_suggestions(self, *, direction: str, item_id: str) -> dict[str, Any]:
        direction = direction.lower().strip()
        if direction not in {"input", "output"}:
            return {
                "direction": direction,
                "item_id": item_id,
                "suggestions": [],
                "error": "direction must be input or output",
            }

        if direction == "input":
            where = "r.output_item_id = ?"
            order = "b.name COLLATE NOCASE, r.recipe_level, r.recipe_id"
        else:
            where = """
                EXISTS (
                    SELECT 1
                    FROM recipe_inputs ri
                    WHERE ri.recipe_key = r.recipe_key
                      AND ri.input_item_id = ?
                )
            """
            order = "oi.name COLLATE NOCASE, b.name COLLATE NOCASE, r.recipe_level, r.recipe_id"

        with connection(self.cfg.db_path) as conn:
            rows = conn.execute(
                f"""
                SELECT r.*, b.name AS building_name, b.category AS building_category,
                       b.family_name AS building_family_name, b.tier AS building_tier,
                       b.image_url AS building_image_url,
                       oi.name AS output_item_name, oi.image_url AS output_item_image_url
                FROM recipes r
                JOIN buildings b ON b.building_id = r.building_id
                JOIN items oi ON oi.item_id = r.output_item_id
                WHERE {where}
                ORDER BY {order}
                """,
                (item_id,),
            ).fetchall()
            suggestions = [self._planner_recipe_payload(conn, row) for row in rows]

        return {
            "direction": direction,
            "item_id": item_id,
            "suggestions": suggestions,
        }

    def get_item_detail(self, item_id: str) -> dict[str, Any]:
        with connection(self.cfg.db_path) as conn:
            item = conn.execute(
                "SELECT * FROM items WHERE item_id = ?", (item_id,)
            ).fetchone()
            if not item:
                raise DataNotFoundError(item_id)
            produced_by = self._produced_by(conn, item_id)
            used_in = self._used_in(conn, item_id)
            unlock_requirements = self._unlock_requirements_for_item(conn, item_id)
            used_to_unlock = self._used_to_unlock(conn, item_id)
        return {
            "item": self._item_payload(item),
            "unlock_requirements": unlock_requirements,
            "used_to_unlock": used_to_unlock,
            "produced_by": produced_by,
            "used_in": used_in,
            "meta": {
                "producer_count": len(produced_by),
                "usage_count": len(used_in),
                "unlock_recipe_count": len(unlock_requirements),
                "unlocks_recipe_count": len(used_to_unlock),
            },
        }

    def _produced_by(self, conn: sqlite3.Connection, item_id: str) -> list[dict[str, Any]]:
        recipe_rows = conn.execute(
            """
            SELECT r.*, b.name AS building_name, b.category AS building_category,
                   b.image_url AS building_image_url
            FROM recipes r
            JOIN buildings b ON b.building_id = r.building_id
            WHERE r.output_item_id = ?
            ORDER BY b.name COLLATE NOCASE, r.recipe_level, r.recipe_id
            """,
            (item_id,),
        ).fetchall()
        return [self._producer_payload(conn, row) for row in recipe_rows]

    def _used_in(self, conn: sqlite3.Connection, item_id: str) -> list[dict[str, Any]]:
        rows = conn.execute(
            """
            SELECT u.*, r.recipe_id, r.recipe_level, r.duration_seconds,
                   r.output_item_id, r.output_quantity, r.output_per_minute,
                   r.original_rate_text, b.building_id, b.name AS building_name,
                   b.category AS building_category, oi.name AS output_item_name,
                   oi.image_url AS output_item_image_url
            FROM item_usages u
            JOIN recipes r ON r.recipe_key = u.recipe_key
            JOIN buildings b ON b.building_id = r.building_id
            JOIN items oi ON oi.item_id = r.output_item_id
            WHERE u.item_id = ?
            ORDER BY b.name COLLATE NOCASE, r.recipe_level, r.recipe_id
            """,
            (item_id,),
        ).fetchall()
        return [
            {
                "building_id": row["building_id"],
                "building_name": row["building_name"],
                "building_category": row["building_category"],
                "recipe_id": row["recipe_id"],
                "recipe_level": row["recipe_level"],
                "consumed_quantity_per_cycle": clean_number(row["consumed_quantity_per_cycle"]),
                "consumed_per_minute": clean_number(row["consumed_per_minute"]),
                "output_item_id": row["output_item_id"],
                "output_item_name": row["output_item_name"],
                "output_item_image_url": row["output_item_image_url"],
                "output_quantity": clean_number(row["output_quantity"]),
                "duration_seconds": clean_number(row["duration_seconds"]),
                "items_per_minute": clean_number(row["output_per_minute"]),
                "original_rate_text": row["original_rate_text"],
            }
            for row in rows
        ]

    def _unlock_requirements_for_item(
        self, conn: sqlite3.Connection, item_id: str
    ) -> list[dict[str, Any]]:
        recipe_rows = conn.execute(
            """
            SELECT r.*, b.name AS building_name, b.category AS building_category,
                   b.image_url AS building_image_url, oi.name AS output_item_name,
                   oi.image_url AS output_item_image_url
            FROM recipes r
            JOIN recipe_unlock_requirements rr ON rr.recipe_key = r.recipe_key
            JOIN buildings b ON b.building_id = r.building_id
            JOIN items oi ON oi.item_id = r.output_item_id
            WHERE r.output_item_id = ?
            GROUP BY r.recipe_key
            ORDER BY b.name COLLATE NOCASE, r.recipe_level, r.recipe_id
            """,
            (item_id,),
        ).fetchall()
        return [self._unlock_recipe_payload(conn, row) for row in recipe_rows]

    def _used_to_unlock(self, conn: sqlite3.Connection, item_id: str) -> list[dict[str, Any]]:
        rows = conn.execute(
            """
            SELECT uu.*, r.recipe_id, r.recipe_level, r.duration_seconds,
                   r.output_item_id, r.output_quantity, r.output_per_minute,
                   r.original_rate_text, b.building_id, b.name AS building_name,
                   b.category AS building_category, oi.name AS output_item_name,
                   oi.image_url AS output_item_image_url
            FROM item_unlock_usages uu
            JOIN recipes r ON r.recipe_key = uu.recipe_key
            JOIN buildings b ON b.building_id = r.building_id
            JOIN items oi ON oi.item_id = r.output_item_id
            WHERE uu.item_id = ?
            ORDER BY oi.name COLLATE NOCASE, b.name COLLATE NOCASE, r.recipe_level, r.recipe_id
            """,
            (item_id,),
        ).fetchall()
        return [
            {
                "required_quantity": clean_number(row["required_quantity"]),
                "building_id": row["building_id"],
                "building_name": row["building_name"],
                "building_category": row["building_category"],
                "recipe_id": row["recipe_id"],
                "recipe_level": row["recipe_level"],
                "output_item_id": row["output_item_id"],
                "output_item_name": row["output_item_name"],
                "output_item_image_url": row["output_item_image_url"],
                "output_quantity": clean_number(row["output_quantity"]),
                "duration_seconds": clean_number(row["duration_seconds"]),
                "items_per_minute": clean_number(row["output_per_minute"]),
                "original_rate_text": row["original_rate_text"],
            }
            for row in rows
        ]

    def _producer_payload(self, conn: sqlite3.Connection, row: sqlite3.Row) -> dict[str, Any]:
        inputs = conn.execute(
            """
            SELECT ri.*, i.name, i.image_url
            FROM recipe_inputs ri
            LEFT JOIN items i ON i.item_id = ri.input_item_id
            WHERE ri.recipe_key = ?
            ORDER BY i.name COLLATE NOCASE, ri.input_item_id
            """,
            (row["recipe_key"],),
        ).fetchall()
        unlocks = conn.execute(
            """
            SELECT rr.*, i.name, i.image_url
            FROM recipe_unlock_requirements rr
            LEFT JOIN items i ON i.item_id = rr.item_id
            WHERE rr.recipe_key = ?
            ORDER BY i.name COLLATE NOCASE, rr.item_id
            """,
            (row["recipe_key"],),
        ).fetchall()
        return {
            "building_id": row["building_id"],
            "building_name": row["building_name"],
            "building_category": row["building_category"],
            "building_image_url": row["building_image_url"],
            "recipe_id": row["recipe_id"],
            "recipe_level": row["recipe_level"],
            "output_quantity": clean_number(row["output_quantity"]),
            "duration_seconds": clean_number(row["duration_seconds"]),
            "original_rate_text": row["original_rate_text"],
            "items_per_minute": clean_number(row["output_per_minute"]),
            "inputs": [
                {
                    "item_id": entry["input_item_id"],
                    "name": entry["name"],
                    "image_url": entry["image_url"],
                    "quantity_per_cycle": clean_number(entry["input_quantity"]),
                    "quantity_per_minute": clean_number(entry["input_per_minute"]),
                }
                for entry in inputs
            ],
            "unlock_requirements": [
                {
                    "item_id": entry["item_id"],
                    "name": entry["name"],
                    "image_url": entry["image_url"],
                    "required_quantity": clean_number(entry["required_quantity"]),
                }
                for entry in unlocks
            ],
        }

    def _planner_recipe_payload(self, conn: sqlite3.Connection, row: sqlite3.Row) -> dict[str, Any]:
        inputs = conn.execute(
            """
            SELECT ri.*, i.name, i.image_url
            FROM recipe_inputs ri
            LEFT JOIN items i ON i.item_id = ri.input_item_id
            WHERE ri.recipe_key = ?
            ORDER BY i.name COLLATE NOCASE, ri.input_item_id
            """,
            (row["recipe_key"],),
        ).fetchall()
        unlocks = conn.execute(
            """
            SELECT rr.*, i.name, i.image_url
            FROM recipe_unlock_requirements rr
            LEFT JOIN items i ON i.item_id = rr.item_id
            WHERE rr.recipe_key = ?
            ORDER BY i.name COLLATE NOCASE, rr.item_id
            """,
            (row["recipe_key"],),
        ).fetchall()
        return {
            "recipe_key": row["recipe_key"],
            "recipe_id": row["recipe_id"],
            "recipe_level": row["recipe_level"],
            "building_id": row["building_id"],
            "building_name": row["building_name"],
            "building_category": row["building_category"],
            "building_family_name": row["building_family_name"],
            "building_tier": row["building_tier"],
            "building_image_url": row["building_image_url"],
            "duration_seconds": clean_number(row["duration_seconds"]),
            "output": {
                "item_id": row["output_item_id"],
                "name": row["output_item_name"],
                "image_url": row["output_item_image_url"],
                "quantity_per_cycle": clean_number(row["output_quantity"]),
                "quantity_per_minute": clean_number(row["output_per_minute"]),
            },
            "original_rate_text": row["original_rate_text"],
            "inputs": [
                {
                    "item_id": entry["input_item_id"],
                    "name": entry["name"],
                    "image_url": entry["image_url"],
                    "quantity_per_cycle": clean_number(entry["input_quantity"]),
                    "quantity_per_minute": clean_number(entry["input_per_minute"]),
                }
                for entry in inputs
            ],
            "unlock_requirements": [
                {
                    "item_id": entry["item_id"],
                    "name": entry["name"],
                    "image_url": entry["image_url"],
                    "required_quantity": clean_number(entry["required_quantity"]),
                }
                for entry in unlocks
            ],
        }

    def _unlock_recipe_payload(self, conn: sqlite3.Connection, row: sqlite3.Row) -> dict[str, Any]:
        requirements = conn.execute(
            """
            SELECT rr.*, i.name, i.image_url
            FROM recipe_unlock_requirements rr
            LEFT JOIN items i ON i.item_id = rr.item_id
            WHERE rr.recipe_key = ?
            ORDER BY i.name COLLATE NOCASE, rr.item_id
            """,
            (row["recipe_key"],),
        ).fetchall()
        return {
            "building_id": row["building_id"],
            "building_name": row["building_name"],
            "building_category": row["building_category"],
            "building_image_url": row["building_image_url"],
            "recipe_id": row["recipe_id"],
            "recipe_level": row["recipe_level"],
            "output_item_id": row["output_item_id"],
            "output_item_name": row["output_item_name"],
            "output_item_image_url": row["output_item_image_url"],
            "output_quantity": clean_number(row["output_quantity"]),
            "duration_seconds": clean_number(row["duration_seconds"]),
            "items_per_minute": clean_number(row["output_per_minute"]),
            "original_rate_text": row["original_rate_text"],
            "required_items": [
                {
                    "item_id": entry["item_id"],
                    "name": entry["name"],
                    "image_url": entry["image_url"],
                    "required_quantity": clean_number(entry["required_quantity"]),
                }
                for entry in requirements
            ],
        }

    def _item_payload(self, row: sqlite3.Row) -> dict[str, Any]:
        return {
            "item_id": row["item_id"],
            "name": row["name"],
            "description": row["description"],
            "category": row["category"],
            "stack": row["stack"],
            "level": row["level"],
            "source_url": row["source_url"],
            "image_url": row["image_url"],
            "image_source_url": row["image_source_url"],
        }

    def _item_summary(self, row: sqlite3.Row) -> dict[str, Any]:
        payload = self._item_payload(row)
        payload["produced"] = bool(row["produced"])
        payload["used"] = bool(row["used"])
        payload["requires_unlock"] = bool(row["requires_unlock"])
        payload["used_to_unlock"] = bool(row["used_to_unlock"])
        return payload

    def _building_payload(self, row: sqlite3.Row) -> dict[str, Any]:
        return {
            "building_id": row["building_id"],
            "name": row["name"],
            "family_name": row["family_name"],
            "tier": row["tier"],
            "category": row["category"],
            "description": row["description"],
            "power": None if row["power"] is None else clean_number(row["power"]),
            "temperature": None if row["temperature"] is None else clean_number(row["temperature"]),
            "source_url": row["source_url"],
            "image_url": row["image_url"],
            "image_source_url": row["image_source_url"],
            "recipe_count": row["recipe_count"],
        }

    def _corporations_payload(self, conn: sqlite3.Connection) -> list[dict[str, Any]]:
        rows = conn.execute(
            """
            SELECT *
            FROM corporations
            ORDER BY name COLLATE NOCASE, corporation_id
            """
        ).fetchall()
        payload: list[dict[str, Any]] = []
        for row in rows:
            levels = conn.execute(
                """
                SELECT *
                FROM corporation_levels
                WHERE corporation_id = ?
                ORDER BY level
                """,
                (row["corporation_id"],),
            ).fetchall()
            payload.append(
                {
                    "corporation_id": row["corporation_id"],
                    "name": row["name"],
                    "description": row["description"],
                    "source_url": row["source_url"],
                    "icon_url": self._site_asset_url(row["icon_url"]),
                    "colour": row["colour"],
                    "max_level": row["max_level"],
                    "levels": [
                        {
                            "level": level["level"],
                            "reputation": level["reputation"],
                            "building_rewards": self._corporation_building_rewards(
                                conn,
                                row["corporation_id"],
                                level["level"],
                            ),
                            "item_rewards": self._corporation_item_rewards(
                                conn,
                                row["corporation_id"],
                                level["level"],
                            ),
                        }
                        for level in levels
                    ],
                }
            )
        return payload

    def _corporation_building_rewards(
        self,
        conn: sqlite3.Connection,
        corporation_id: str,
        level: int,
    ) -> list[dict[str, Any]]:
        rows = conn.execute(
            """
            SELECT *
            FROM corporation_building_rewards
            WHERE corporation_id = ? AND level = ?
            ORDER BY name COLLATE NOCASE, building_id
            """,
            (corporation_id, level),
        ).fetchall()
        return [
            {
                "building_id": row["building_id"],
                "name": row["name"],
                "category": row["category"],
                "icon_url": self._site_asset_url(row["icon_url"]),
            }
            for row in rows
        ]

    def _corporation_item_rewards(
        self,
        conn: sqlite3.Connection,
        corporation_id: str,
        level: int,
    ) -> list[dict[str, Any]]:
        rows = conn.execute(
            """
            SELECT *
            FROM corporation_item_rewards
            WHERE corporation_id = ? AND level = ?
            ORDER BY name COLLATE NOCASE, item_id
            """,
            (corporation_id, level),
        ).fetchall()
        return [
            {
                "item_id": row["item_id"],
                "name": row["name"],
                "category": row["category"],
                "icon_url": self._site_asset_url(row["icon_url"]),
            }
            for row in rows
        ]

    def _building_unlocks_payload(self, conn: sqlite3.Connection) -> dict[str, list[dict[str, Any]]]:
        rows = conn.execute(
            """
            SELECT cbr.building_id, cbr.corporation_id, c.name AS corporation_name,
                   MIN(cbr.level) AS level
            FROM corporation_building_rewards cbr
            JOIN corporations c ON c.corporation_id = cbr.corporation_id
            GROUP BY cbr.building_id, cbr.corporation_id, c.name
            ORDER BY cbr.building_id, level, c.name COLLATE NOCASE
            """
        ).fetchall()
        result: dict[str, list[dict[str, Any]]] = {}
        for row in rows:
            result.setdefault(row["building_id"], []).append(
                {
                    "corporation_id": row["corporation_id"],
                    "corporation_name": row["corporation_name"],
                    "level": row["level"],
                }
            )
        return result

    def _site_asset_url(self, value: str | None) -> str | None:
        if not value:
            return None
        if value.startswith("http"):
            return value
        return f"{self.cfg.base_url}{value}"

    def _refresh_run_payload(self, row: sqlite3.Row) -> dict[str, Any]:
        return {
            "run_id": row["run_id"],
            "status": row["status"],
            "started_at": row["started_at"],
            "finished_at": row["finished_at"],
            "item_count": row["item_count"],
            "building_count": row["building_count"],
            "recipe_count": row["recipe_count"],
            "warnings": json.loads(row["warnings_json"] or "[]"),
        }
