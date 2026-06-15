from __future__ import annotations

import json
from pathlib import Path
from typing import Any


DEFAULT_LANGUAGE = "en"
SUPPORTED_LANGUAGES = ("en", "ru", "de", "uk")


def normalize_language(language: str | None) -> str:
    if not language:
        return DEFAULT_LANGUAGE
    code = language.strip().lower().replace("_", "-").split("-", 1)[0]
    return code if code in SUPPORTED_LANGUAGES else DEFAULT_LANGUAGE


class LocalizationStore:
    def __init__(self, localization_dir: Path) -> None:
        self.localization_dir = localization_dir
        self._packs = {
            language: self._load(language)
            for language in SUPPORTED_LANGUAGES
        }

    @property
    def languages(self) -> list[dict[str, str]]:
        labels = {
            "en": "English",
            "ru": "Russian",
            "de": "German",
            "uk": "Ukrainian",
        }
        return [{"code": code, "name": labels[code]} for code in SUPPORTED_LANGUAGES]

    def _load(self, language: str) -> dict[str, Any]:
        path = self.localization_dir / f"{language}.json"
        if not path.exists():
            return {}
        with path.open("r", encoding="utf-8") as handle:
            payload = json.load(handle)
        return payload if isinstance(payload, dict) else {}

    def text(
        self,
        language: str | None,
        section: str,
        key: str | None,
        field: str,
        fallback: Any,
    ) -> Any:
        if not key:
            return fallback
        lang = normalize_language(language)
        if lang == DEFAULT_LANGUAGE:
            return self._lookup(DEFAULT_LANGUAGE, section, key, field, fallback)
        localized = self._lookup(lang, section, key, field, None)
        if localized not in (None, ""):
            return localized
        return self._lookup(DEFAULT_LANGUAGE, section, key, field, fallback)

    def _lookup(
        self,
        language: str,
        section: str,
        key: str,
        field: str,
        fallback: Any,
    ) -> Any:
        entry = self._packs.get(language, {}).get(section, {}).get(key)
        if isinstance(entry, dict):
            value = entry.get(field)
            return fallback if value in (None, "") else value
        return fallback

    def ui(self, language: str | None, key: str, fallback: str) -> str:
        lang = normalize_language(language)
        value = self._packs.get(lang, {}).get("ui", {}).get(key)
        if isinstance(value, str) and value:
            return value
        value = self._packs.get(DEFAULT_LANGUAGE, {}).get("ui", {}).get(key)
        return value if isinstance(value, str) and value else fallback
