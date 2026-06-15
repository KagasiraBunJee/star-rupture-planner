from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path


BASE_DIR = Path(__file__).resolve().parent.parent


@dataclass(frozen=True)
class Settings:
    base_url: str = "https://starrupture.tools"
    cdn_base_url: str = "https://cdn.pixel6.tools/starrupture/images"
    image_version: str = "1775754775"
    data_dir: Path = BASE_DIR / "data"
    asset_dir: Path = BASE_DIR / "data" / "assets"
    db_path: Path = BASE_DIR / "data" / "starrupture.sqlite3"
    localization_dir: Path = BASE_DIR / "data" / "localization"
    transport_tiers_path: Path = BASE_DIR / "data" / "transport_tiers.json"
    host: str = "127.0.0.1"
    port: int = 8010
    request_timeout_seconds: int = 30
    user_agent: str = "SR_DB local crawler/1.0 (+https://starrupture.tools)"

    @property
    def item_asset_dir(self) -> Path:
        return self.asset_dir / "items"

    @property
    def building_asset_dir(self) -> Path:
        return self.asset_dir / "buildings"


settings = Settings()
