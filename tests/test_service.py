from __future__ import annotations

import unittest

from starrupture_api.config import settings
from starrupture_api.service import ResourceService


class ServiceFixtureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.service = ResourceService(settings)
        meta = cls.service.get_meta()
        if meta["counts"]["items"] == 0:
            raise unittest.SkipTest("dataset has not been refreshed")

    def test_meta_counts(self) -> None:
        meta = self.service.get_meta()
        self.assertEqual(meta["counts"]["items"], 135)
        self.assertGreaterEqual(meta["counts"]["buildings"], 20)
        self.assertGreaterEqual(meta["counts"]["recipes"], 100)

    def test_rotor_production_examples(self) -> None:
        detail = self.service.get_item_detail("rotor")
        producers = {
            (entry["building_name"], entry["recipe_id"]): entry
            for entry in detail["produced_by"]
        }
        self.assertEqual(producers[("Fabricator", "rotor")]["items_per_minute"], 10)
        self.assertEqual(producers[("Fabricator", "rotor")]["original_rate_text"], "1 per 6 seconds")
        self.assertEqual(
            producers[("Fabricator v.2", "rotor-v2")]["items_per_minute"],
            60,
        )
        self.assertTrue(detail["used_in"])

    def test_rotor_unlock_requirements_are_top_level(self) -> None:
        detail = self.service.get_item_detail("rotor")
        unlocks = {
            entry["recipe_id"]: {
                required["item_id"]: required["required_quantity"]
                for required in entry["required_items"]
            }
            for entry in detail["unlock_requirements"]
        }
        self.assertIn("rotor", unlocks)
        self.assertEqual(unlocks["rotor"]["rotor-blueprint"], 1)
        self.assertEqual(unlocks["rotor"]["data-point"], 400)

    def test_data_point_reverse_unlock_usage(self) -> None:
        detail = self.service.get_item_detail("data-point")
        recipes = {entry["recipe_id"]: entry for entry in detail["used_to_unlock"]}
        self.assertIn("rotor", recipes)
        self.assertEqual(recipes["rotor"]["required_quantity"], 400)

    def test_converter_production_example(self) -> None:
        detail = self.service.get_item_detail("converter")
        facturer = [entry for entry in detail["produced_by"] if entry["building_name"] == "Facturer"]
        self.assertEqual(len(facturer), 1)
        self.assertEqual(facturer[0]["original_rate_text"], "1 per 15 seconds")
        self.assertEqual(facturer[0]["items_per_minute"], 4)
        self.assertTrue(facturer[0]["unlock_requirements"])


if __name__ == "__main__":
    unittest.main()
