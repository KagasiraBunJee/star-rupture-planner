from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


ItemDict = dict[str, Any]


@dataclass
class RecipeInput:
    item: ItemDict
    quantity: float


@dataclass
class RecipeRequirement:
    item: ItemDict
    quantity: float


@dataclass
class RecipeRecord:
    recipe_key: str
    recipe_id: str
    building_id: str
    level: int | None
    duration_seconds: float
    output_item: ItemDict
    output_quantity: float
    inputs: list[RecipeInput] = field(default_factory=list)
    unlock_requirements: list[RecipeRequirement] = field(default_factory=list)


@dataclass
class BuildingRecord:
    building: ItemDict
    recipes: list[RecipeRecord]


@dataclass
class RefreshSummary:
    run_id: int | None
    status: str
    started_at: str
    finished_at: str | None
    item_count: int = 0
    building_count: int = 0
    recipe_count: int = 0
    warnings: list[str] = field(default_factory=list)

