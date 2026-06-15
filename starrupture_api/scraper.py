from __future__ import annotations

import json
import re
import time
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from .config import Settings, settings
from .models import BuildingRecord, RecipeInput, RecipeRecord, RecipeRequirement
from .utils import (
    PLACEHOLDER_RESOURCE_IDS,
    RELEVANT_BUILDING_CATEGORIES,
    extension_from_url,
    image_source_url,
    local_asset_url,
    original_rate_text,
    rate_per_minute,
    slugify_filename,
)


@dataclass
class ScrapeResult:
    items: dict[str, dict[str, Any]]
    buildings: dict[str, dict[str, Any]]
    recipes: list[RecipeRecord]
    warnings: list[str] = field(default_factory=list)


class StarRuptureScraper:
    def __init__(self, cfg: Settings = settings) -> None:
        self.cfg = cfg

    def scrape(self, download_images: bool = True) -> ScrapeResult:
        search_index = self._get_json("/api/search")
        clean_items = self._clean_resource_items(search_index)
        item_ids = set(clean_items)
        relevant_buildings = [
            entry
            for entry in search_index
            if entry.get("type") == "building"
            and entry.get("category") in RELEVANT_BUILDING_CATEGORIES
        ]

        warnings: list[str] = []
        items: dict[str, dict[str, Any]] = {}
        buildings: dict[str, dict[str, Any]] = {}
        recipes: list[RecipeRecord] = []

        for item_id, item_ref in clean_items.items():
            try:
                item = self._fetch_item(item_ref["url"])
            except Exception as exc:
                warnings.append(f"Failed to fetch item {item_id}: {exc}")
                item = dict(item_ref)
            if item.get("url") != item_ref.get("url") or item.get("category") != "resource":
                item = {**item, **item_ref}
            item = self._normalize_item(item)
            if download_images:
                self._download_asset(item, "items")
            items[item_id] = item
            time.sleep(0.02)

        for building_ref in relevant_buildings:
            building_id = building_ref["id"]
            try:
                record = self._fetch_building(building_ref["url"])
            except Exception as exc:
                warnings.append(f"Failed to fetch building {building_id}: {exc}")
                continue

            resource_recipes = [
                recipe
                for recipe in record.recipes
                if recipe.output_item.get("id") in item_ids
                and recipe.output_item.get("category") == "resource"
            ]
            if not resource_recipes:
                continue

            building = self._normalize_building(record.building)
            if download_images:
                self._download_asset(building, "buildings")
            buildings[building["id"]] = building
            recipes.extend(resource_recipes)
            time.sleep(0.05)

        self._ensure_recipe_side_items(items, recipes, download_images)
        return ScrapeResult(items=items, buildings=buildings, recipes=recipes, warnings=warnings)

    def _clean_resource_items(self, search_index: list[dict[str, Any]]) -> dict[str, dict[str, Any]]:
        return {
            entry["id"]: entry
            for entry in search_index
            if entry.get("type") == "item"
            and entry.get("category") == "resource"
            and entry.get("id") not in PLACEHOLDER_RESOURCE_IDS
        }

    def _fetch_item(self, url_path: str) -> dict[str, Any]:
        html = self._get_text(url_path)
        decoded = self._decode_next_flight(html)
        item_id = url_path.rstrip("/").split("/")[-1]
        id_match: dict[str, Any] | None = None
        for match in re.finditer(r'"item":', decoded):
            start = match.end()
            if start >= len(decoded) or decoded[start] != "{":
                continue
            candidate = json.loads(self._balanced_json_object(decoded, start))
            if candidate.get("url") == url_path:
                return candidate
            if candidate.get("id") == item_id and id_match is None:
                id_match = candidate
        if id_match is not None:
            return id_match
        raise ValueError(f"Could not find item object for {item_id}")

    def _fetch_building(self, url_path: str) -> BuildingRecord:
        html = self._get_text(url_path)
        decoded = self._decode_next_flight(html)
        building = self._extract_object_after(decoded, '"building":')
        recipes = [
            self._normalize_recipe(building, recipe)
            for recipe in building.get("recipes", [])
            if recipe.get("output") and recipe.get("duration")
        ]
        return BuildingRecord(building=building, recipes=recipes)

    def _normalize_recipe(self, building: dict[str, Any], raw: dict[str, Any]) -> RecipeRecord:
        output_item = self._resolve_building_ref(building, raw["output"]["item"])
        output_quantity = float(raw["output"].get("quantity", 1))
        duration = float(raw.get("duration", 0))
        recipe_key = f"{building['id']}::{raw['id']}"

        inputs = [
            RecipeInput(
                item=self._resolve_building_ref(building, entry["item"]),
                quantity=float(entry.get("quantity", 0)),
            )
            for entry in raw.get("inputs", [])
        ]
        unlocks = [
            RecipeRequirement(
                item=self._resolve_building_ref(building, entry["item"]),
                quantity=float(entry.get("quantity", 0)),
            )
            for entry in raw.get("research", [])
        ]
        return RecipeRecord(
            recipe_key=recipe_key,
            recipe_id=raw["id"],
            building_id=building["id"],
            level=raw.get("level"),
            duration_seconds=duration,
            output_item=output_item,
            output_quantity=output_quantity,
            inputs=inputs,
            unlock_requirements=unlocks,
        )

    def _normalize_item(self, item: dict[str, Any]) -> dict[str, Any]:
        normalized = dict(item)
        normalized["source_url"] = f"{self.cfg.base_url}{item.get('url', '')}"
        normalized["image_source_url"] = image_source_url(item.get("icon"), self.cfg)
        normalized.setdefault("description", "")
        return normalized

    def _normalize_building(self, building: dict[str, Any]) -> dict[str, Any]:
        normalized = dict(building)
        normalized["source_url"] = f"{self.cfg.base_url}{building.get('url', '')}"
        normalized["image_source_url"] = image_source_url(building.get("icon"), self.cfg)
        normalized["family_name"], normalized["tier"] = self._building_family_and_tier(
            building.get("name", "")
        )
        normalized.setdefault("description", "")
        return normalized

    def _building_family_and_tier(self, name: str) -> tuple[str, int | None]:
        match = re.search(r"\s+v\.(\d+)\s*$", name)
        if not match:
            return name.strip(), 1
        return name[: match.start()].strip(), int(match.group(1))

    def _ensure_recipe_side_items(
        self,
        items: dict[str, dict[str, Any]],
        recipes: list[RecipeRecord],
        download_images: bool,
    ) -> None:
        for recipe in recipes:
            related = [entry.item for entry in recipe.inputs]
            related.extend(entry.item for entry in recipe.unlock_requirements)
            related.append(recipe.output_item)
            for item in related:
                item_id = item.get("id")
                if not item_id or item_id in items:
                    continue
                normalized = self._normalize_item(item)
                if download_images:
                    self._download_asset(normalized, "items")
                items[item_id] = normalized

    def _download_asset(self, record: dict[str, Any], kind: str) -> None:
        source_url = record.get("image_source_url")
        if not source_url:
            record["image_path"] = None
            record["image_url"] = None
            return
        asset_dir = self.cfg.item_asset_dir if kind == "items" else self.cfg.building_asset_dir
        asset_dir.mkdir(parents=True, exist_ok=True)
        filename = f"{slugify_filename(record['id'])}{extension_from_url(source_url)}"
        target = asset_dir / filename
        if not target.exists():
            try:
                target.write_bytes(self._get_bytes(source_url))
            except Exception:
                record["image_path"] = None
                record["image_url"] = None
                return
        record["image_path"] = str(target)
        record["image_url"] = local_asset_url(kind, str(target))

    def _get_json(self, url_path: str) -> Any:
        return json.loads(self._get_text(url_path))

    def _get_text(self, url_path_or_url: str) -> str:
        return self._get_bytes(url_path_or_url).decode("utf-8")

    def _get_bytes(self, url_path_or_url: str) -> bytes:
        url = (
            url_path_or_url
            if url_path_or_url.startswith("http")
            else f"{self.cfg.base_url}{url_path_or_url}"
        )
        req = Request(url, headers={"User-Agent": self.cfg.user_agent})
        try:
            with urlopen(req, timeout=self.cfg.request_timeout_seconds) as response:
                return response.read()
        except HTTPError as exc:
            raise RuntimeError(f"HTTP {exc.code} for {url}") from exc
        except URLError as exc:
            raise RuntimeError(f"Network error for {url}: {exc.reason}") from exc

    def _decode_next_flight(self, html: str) -> str:
        chunks = re.findall(r"self\.__next_f\.push\(\[1,\"(.*?)\"\]\)</script>", html)
        decoded: list[str] = []
        for chunk in chunks:
            decoded.append(json.loads(f'"{chunk}"'))
        return "".join(decoded)

    def _extract_object_after(self, text: str, key: str) -> dict[str, Any]:
        index = text.find(key)
        if index < 0:
            raise ValueError(f"Could not find {key}")
        start = index + len(key)
        return json.loads(self._balanced_json_object(text, start))

    def _balanced_json_object(self, text: str, start: int) -> str:
        if text[start] != "{":
            raise ValueError("Expected JSON object")
        depth = 0
        in_string = False
        escaped = False
        for index in range(start, len(text)):
            char = text[index]
            if in_string:
                if escaped:
                    escaped = False
                elif char == "\\":
                    escaped = True
                elif char == '"':
                    in_string = False
            else:
                if char == '"':
                    in_string = True
                elif char == "{":
                    depth += 1
                elif char == "}":
                    depth -= 1
                    if depth == 0:
                        return text[start : index + 1]
        raise ValueError("Unbalanced JSON object")

    def _resolve_building_ref(self, building: dict[str, Any], value: Any) -> Any:
        if not (isinstance(value, str) and "building:" in value):
            return value
        current: Any = building
        for part in value.split("building:", 1)[1].split(":"):
            current = current[int(part)] if part.isdigit() else current[part]
        return current


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def recipe_rate_payload(recipe: RecipeRecord) -> dict[str, Any]:
    return {
        "output_per_minute": rate_per_minute(recipe.output_quantity, recipe.duration_seconds),
        "original_rate_text": original_rate_text(recipe.output_quantity, recipe.duration_seconds),
    }
