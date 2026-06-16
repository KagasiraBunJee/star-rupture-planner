from __future__ import annotations

import sys
from dataclasses import dataclass
from pathlib import Path


def _base_dir() -> Path:
    if getattr(sys, "frozen", False):
        executable_dir = Path(sys.executable).resolve().parent
        return executable_dir.parent if executable_dir.name.lower() == "api" else executable_dir

    return Path(__file__).resolve().parent.parent


BASE_DIR = _base_dir()


@dataclass(frozen=True)
class Settings:
    source_site_url: str = "https://starrupture.tools"
    data_dir: Path = BASE_DIR / "data"
    asset_dir: Path = BASE_DIR / "data" / "assets"
    db_path: Path = BASE_DIR / "data" / "starrupture.sqlite3"
    localization_dir: Path = BASE_DIR / "data" / "localization"
    transport_tiers_path: Path = BASE_DIR / "data" / "transport_tiers.json"
    host: str = "127.0.0.1"
    port: int = 8010
    request_timeout_seconds: int = 30

    @property
    def item_asset_dir(self) -> Path:
        return self.asset_dir / "items"

    @property
    def building_asset_dir(self) -> Path:
        return self.asset_dir / "buildings"


settings = Settings()
