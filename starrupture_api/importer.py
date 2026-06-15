from __future__ import annotations

import sqlite3

from .config import Settings, settings
from .db import begin_refresh, connection, finish_refresh, init_db, reset_dataset
from .models import RefreshSummary
from .scraper import ScrapeResult, StarRuptureScraper, utc_now_iso
from .utils import PLACEHOLDER_RESOURCE_IDS, original_rate_text, rate_per_minute


class Importer:
    def __init__(self, cfg: Settings = settings) -> None:
        self.cfg = cfg

    def refresh(self, *, download_images: bool = True) -> RefreshSummary:
        started_at = utc_now_iso()
        run_id: int | None = None
        warnings: list[str] = []
        with connection(self.cfg.db_path) as conn:
            init_db(conn)
            run_id = begin_refresh(conn, started_at)
            try:
                result = StarRuptureScraper(self.cfg).scrape(download_images=download_images)
                reset_dataset(conn)
                self._write_result(conn, result)
                finished_at = utc_now_iso()
                finish_refresh(
                    conn,
                    run_id,
                    status="success",
                    finished_at=finished_at,
                    item_count=self._clean_resource_count(result),
                    building_count=len(result.buildings),
                    recipe_count=len(result.recipes),
                    warnings=result.warnings,
                )
                return RefreshSummary(
                    run_id=run_id,
                    status="success",
                    started_at=started_at,
                    finished_at=finished_at,
                    item_count=self._clean_resource_count(result),
                    building_count=len(result.buildings),
                    recipe_count=len(result.recipes),
                    warnings=result.warnings,
                )
            except Exception as exc:
                warnings.append(str(exc))
                finished_at = utc_now_iso()
                finish_refresh(
                    conn,
                    run_id,
                    status="failed",
                    finished_at=finished_at,
                    item_count=0,
                    building_count=0,
                    recipe_count=0,
                    warnings=warnings,
                )
                raise

    def _clean_resource_count(self, result: ScrapeResult) -> int:
        return sum(
            1
            for item in result.items.values()
            if item.get("category") == "resource"
            and item.get("id") not in PLACEHOLDER_RESOURCE_IDS
        )

    def _write_result(self, conn: sqlite3.Connection, result: ScrapeResult) -> None:
        for item in result.items.values():
            conn.execute(
                """
                INSERT OR REPLACE INTO items (
                    item_id, name, description, category, stack, level, source_url,
                    image_source_url, image_path, image_url
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    item["id"],
                    item.get("name"),
                    item.get("description"),
                    item.get("category"),
                    item.get("stack"),
                    item.get("level"),
                    item.get("source_url"),
                    item.get("image_source_url"),
                    item.get("image_path"),
                    item.get("image_url"),
                ),
            )

        for building in result.buildings.values():
            conn.execute(
                """
                INSERT OR REPLACE INTO buildings (
                    building_id, name, family_name, tier, category, description,
                    source_url, image_source_url, image_path, image_url
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    building["id"],
                    building.get("name"),
                    building.get("family_name"),
                    building.get("tier"),
                    building.get("category"),
                    building.get("description"),
                    building.get("source_url"),
                    building.get("image_source_url"),
                    building.get("image_path"),
                    building.get("image_url"),
                ),
            )

        for recipe in result.recipes:
            output_per_minute = rate_per_minute(recipe.output_quantity, recipe.duration_seconds)
            conn.execute(
                """
                INSERT OR REPLACE INTO recipes (
                    recipe_key, recipe_id, building_id, recipe_level, duration_seconds,
                    output_item_id, output_quantity, output_per_minute, original_rate_text
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    recipe.recipe_key,
                    recipe.recipe_id,
                    recipe.building_id,
                    recipe.level,
                    recipe.duration_seconds,
                    recipe.output_item["id"],
                    recipe.output_quantity,
                    output_per_minute,
                    original_rate_text(recipe.output_quantity, recipe.duration_seconds),
                ),
            )
            for entry in recipe.inputs:
                input_per_minute = rate_per_minute(entry.quantity, recipe.duration_seconds)
                conn.execute(
                    """
                    INSERT OR REPLACE INTO recipe_inputs (
                        recipe_key, input_item_id, input_quantity, input_per_minute
                    ) VALUES (?, ?, ?, ?)
                    """,
                    (recipe.recipe_key, entry.item["id"], entry.quantity, input_per_minute),
                )
                conn.execute(
                    """
                    INSERT OR REPLACE INTO item_usages (
                        item_id, recipe_key, consumed_quantity_per_cycle, consumed_per_minute
                    ) VALUES (?, ?, ?, ?)
                    """,
                    (entry.item["id"], recipe.recipe_key, entry.quantity, input_per_minute),
                )
            for entry in recipe.unlock_requirements:
                conn.execute(
                    """
                    INSERT OR REPLACE INTO recipe_unlock_requirements (
                        recipe_key, item_id, required_quantity
                    ) VALUES (?, ?, ?)
                    """,
                    (recipe.recipe_key, entry.item["id"], entry.quantity),
                )
                conn.execute(
                    """
                    INSERT OR REPLACE INTO item_unlock_usages (
                        item_id, recipe_key, required_quantity
                    ) VALUES (?, ?, ?)
                    """,
                    (entry.item["id"], recipe.recipe_key, entry.quantity),
                )

        for entry in result.corporations:
            corporation = entry.corporation
            levels = corporation.get("levels", [])
            conn.execute(
                """
                INSERT OR REPLACE INTO corporations (
                    corporation_id, name, description, source_url, icon_url, colour, max_level
                ) VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    corporation["id"],
                    corporation.get("name"),
                    corporation.get("description"),
                    f"{self.cfg.base_url}{corporation.get('url', '')}",
                    corporation.get("icon"),
                    corporation.get("colour"),
                    max((int(level.get("level", 0)) for level in levels), default=0),
                ),
            )
            for level in levels:
                level_number = int(level.get("level", 0))
                rewards = level.get("rewards", {})
                conn.execute(
                    """
                    INSERT OR REPLACE INTO corporation_levels (
                        corporation_id, level, reputation
                    ) VALUES (?, ?, ?)
                    """,
                    (
                        corporation["id"],
                        level_number,
                        level.get("reputation"),
                    ),
                )
                for building in rewards.get("buildings", []) or []:
                    if not isinstance(building, dict) or not building.get("id"):
                        continue
                    conn.execute(
                        """
                        INSERT OR REPLACE INTO corporation_building_rewards (
                            corporation_id, level, building_id, name, category, icon_url
                        ) VALUES (?, ?, ?, ?, ?, ?)
                        """,
                        (
                            corporation["id"],
                            level_number,
                            building.get("id"),
                            building.get("name"),
                            building.get("category"),
                            building.get("icon"),
                        ),
                    )
                for item in rewards.get("items", []) or []:
                    if not isinstance(item, dict) or not item.get("id"):
                        continue
                    conn.execute(
                        """
                        INSERT OR REPLACE INTO corporation_item_rewards (
                            corporation_id, level, item_id, name, category, icon_url
                        ) VALUES (?, ?, ?, ?, ?, ?)
                        """,
                        (
                            corporation["id"],
                            level_number,
                            item.get("id"),
                            item.get("name"),
                            item.get("category"),
                            item.get("icon"),
                        ),
                    )
