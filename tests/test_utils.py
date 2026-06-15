from __future__ import annotations

import unittest

from starrupture_api.utils import clean_number, original_rate_text, rate_per_minute


class RateTests(unittest.TestCase):
    def test_rate_per_minute(self) -> None:
        self.assertEqual(rate_per_minute(1, 15), 4)
        self.assertEqual(rate_per_minute(1, 6), 10)
        self.assertEqual(rate_per_minute(6, 6), 60)

    def test_original_rate_text(self) -> None:
        self.assertEqual(original_rate_text(1, 15), "1 per 15 seconds")
        self.assertEqual(original_rate_text(6, 6), "6 per 6 seconds")

    def test_clean_number(self) -> None:
        self.assertEqual(clean_number(4.0), 4)
        self.assertEqual(clean_number(1.23456789), 1.234568)


if __name__ == "__main__":
    unittest.main()

