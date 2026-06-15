from __future__ import annotations

import math
import re
from pathlib import Path
from urllib.parse import urlparse

from .config import Settings


PLACEHOLDER_RESOURCE_IDS = {
    "empty-item",
    "placeholder-crafting-ingredient",
    "placeholder-crafting-result",
    "test-resource-item",
}

RELEVANT_BUILDING_CATEGORIES = {
    "crafting",
    "extraction",
    "processing",
    "temperature",
}


def clean_number(value: float) -> int | float:
    if isinstance(value, bool):
        return value
    if math.isfinite(value) and float(value).is_integer():
        return int(value)
    return round(float(value), 6)


def rate_per_minute(quantity: float, duration_seconds: float) -> float:
    if not duration_seconds:
        return 0.0
    return quantity * 60.0 / duration_seconds


def original_rate_text(quantity: float, duration_seconds: float) -> str:
    quantity_text = str(clean_number(quantity))
    duration_text = str(clean_number(duration_seconds))
    unit = "second" if duration_seconds == 1 else "seconds"
    return f"{quantity_text} per {duration_text} {unit}"


def slugify_filename(name: str) -> str:
    stem = re.sub(r"[^a-zA-Z0-9._-]+", "-", name).strip("-").lower()
    return stem or "asset"


def image_source_url(icon_path: str | None, settings: Settings) -> str | None:
    if not icon_path:
        return None
    if icon_path.startswith("http://") or icon_path.startswith("https://"):
        raw = icon_path
    else:
        raw = f"{settings.cdn_base_url.rstrip('/')}/{icon_path.lstrip('/')}"
    separator = "&" if "?" in raw else "?"
    if "v=" not in raw:
        raw = f"{raw}{separator}v={settings.image_version}"
    return raw


def local_asset_url(kind: str, image_path: str | None) -> str | None:
    if not image_path:
        return None
    return f"/assets/{kind}/{Path(image_path).name}"


def extension_from_url(url: str | None, fallback: str = ".webp") -> str:
    if not url:
        return fallback
    suffix = Path(urlparse(url).path).suffix
    return suffix or fallback

