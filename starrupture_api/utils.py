from __future__ import annotations

import math


PLACEHOLDER_RESOURCE_IDS = {
    "empty-item",
    "placeholder-crafting-ingredient",
    "placeholder-crafting-result",
    "test-resource-item",
}

def clean_number(value: float) -> int | float:
    if isinstance(value, bool):
        return value
    if math.isfinite(value) and float(value).is_integer():
        return int(value)
    return round(float(value), 6)
