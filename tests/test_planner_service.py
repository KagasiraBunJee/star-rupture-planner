from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from starrupture_api.config import Settings, settings
from starrupture_api.service import ResourceService
from starrupture_api.scraper import StarRuptureScraper


class PlannerServiceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.service = ResourceService(settings)
        meta = cls.service.get_meta()
        if meta["counts"]["items"] == 0:
            raise unittest.SkipTest("dataset has not been refreshed")

    def test_catalog_contains_graph_ready_recipes(self) -> None:
        catalog = self.service.get_planner_catalog()

        self.assertGreaterEqual(catalog["meta"]["building_count"], 20)
        self.assertGreaterEqual(catalog["meta"]["recipe_count"], 100)

        recipe = next(
            entry
            for entry in catalog["recipes"]
            if entry["building_name"] == "Fabricator"
            and entry["recipe_id"] == "titanium-rod"
        )
        self.assertEqual(recipe["output"]["item_id"], "titanium-rod")
        self.assertEqual(recipe["output"]["quantity_per_minute"], 30)
        self.assertEqual(recipe["inputs"][0]["item_id"], "titanium-bar")
        self.assertEqual(recipe["inputs"][0]["quantity_per_minute"], 30)
        self.assertTrue(recipe["building_image_url"].startswith("/assets/buildings/"))

    def test_extraction_recipes_are_source_nodes(self) -> None:
        catalog = self.service.get_planner_catalog()
        extraction = [
            recipe
            for recipe in catalog["recipes"]
            if recipe["building_category"] == "extraction"
        ]

        self.assertTrue(extraction)
        self.assertTrue(all(len(recipe["inputs"]) == 0 for recipe in extraction))

    def test_input_drag_suggests_producers(self) -> None:
        payload = self.service.get_planner_suggestions(
            direction="input",
            item_id="titanium-bar",
        )

        suggestions = payload["suggestions"]
        self.assertTrue(any(entry["output"]["item_id"] == "titanium-bar" for entry in suggestions))
        self.assertTrue(any(entry["building_name"] == "Smelter" for entry in suggestions))

    def test_output_drag_suggests_consumers(self) -> None:
        payload = self.service.get_planner_suggestions(
            direction="output",
            item_id="titanium-rod",
        )

        suggestions = payload["suggestions"]
        self.assertTrue(suggestions)
        self.assertTrue(
            any(
                any(input_item["item_id"] == "titanium-rod" for input_item in entry["inputs"])
                for entry in suggestions
            )
        )

    def test_missing_transport_tiers_are_explicit(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            cfg = Settings(
                data_dir=settings.data_dir,
                asset_dir=settings.asset_dir,
                db_path=settings.db_path,
                transport_tiers_path=Path(temp_dir) / "missing.json",
            )
            payload = ResourceService(cfg).get_transport_tiers()

        self.assertEqual(payload["tiers"], [])
        self.assertTrue(payload["missing"])

    def test_catalog_contains_corporation_unlock_metadata(self) -> None:
        catalog = self.service.get_planner_catalog()

        self.assertEqual(len(catalog["corporations"]), 6)
        self.assertIn("smelter", catalog["building_unlocks"])
        self.assertTrue(
            any(
                entry["corporation_id"] in {"starting", "selenian"}
                for entry in catalog["building_unlocks"]["smelter"]
            )
        )
        self.assertTrue(catalog["transport_tiers"]["tiers"][0]["unlock_requirements"])

    def test_catalog_contains_building_power_and_temperature(self) -> None:
        catalog = self.service.get_planner_catalog()
        smelter = next(
            building
            for building in catalog["buildings"]
            if building["building_id"] == "smelter"
        )

        self.assertEqual(smelter["power"], -5)
        self.assertEqual(smelter["temperature"], 3)

    def test_catalog_localizes_known_ukrainian_names(self) -> None:
        catalog = self.service.get_planner_catalog(lang="uk")
        recipe = next(
            entry
            for entry in catalog["recipes"]
            if entry["recipe_id"] == "titanium-rod"
        )

        self.assertEqual(recipe["output"]["name"], "Титановий стрижень")
        self.assertEqual(recipe["building_name"], "Фабрикатор")
        self.assertEqual(catalog["transport_tiers"]["tiers"][0]["name"], "Рейки, рівень 1")

    def test_unsupported_language_falls_back_to_english(self) -> None:
        catalog = self.service.get_planner_catalog(lang="zz")
        recipe = next(
            entry
            for entry in catalog["recipes"]
            if entry["recipe_id"] == "titanium-rod"
        )

        self.assertEqual(recipe["output"]["name"], "Titanium Rod")
        self.assertEqual(catalog["meta"]["language"], "en")

    def test_localized_item_search_matches_ukrainian_name(self) -> None:
        payload = self.service.search_items("стрижень", lang="uk")

        self.assertTrue(any(item["item_id"] == "titanium-rod" for item in payload["items"]))

    def test_building_normalization_keeps_power_and_temperature(self) -> None:
        scraper = StarRuptureScraper(settings)
        building = scraper._normalize_building(
            {
                "id": "test-building",
                "name": "Test Building",
                "url": "/buildings/test-building",
                "icon": None,
                "category": "crafting",
                "power": -12,
                "temperature": 7,
            }
        )

        self.assertEqual(building["power"], -12)
        self.assertEqual(building["temperature"], 7)

    def test_corporation_ref_resolver_resolves_training_rewards(self) -> None:
        scraper = StarRuptureScraper(settings)
        root = {
            "corporations": [
                {
                    "id": "source",
                    "levels": [
                        {
                            "rewards": {
                                "buildings": [
                                    {"id": "smelter", "name": "Smelter"},
                                ],
                            },
                        },
                    ],
                },
                {
                    "id": "target",
                    "levels": [
                        {
                            "rewards": {
                                "buildings": [
                                    "$1d:1:props:corporations:0:levels:0:rewards:buildings:0",
                                ],
                            },
                        },
                    ],
                },
            ],
        }

        resolved = scraper._resolve_shared_refs(root["corporations"], root)

        self.assertEqual(
            resolved[1]["levels"][0]["rewards"]["buildings"][0]["id"],
            "smelter",
        )


if __name__ == "__main__":
    unittest.main()
