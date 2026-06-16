from __future__ import annotations

import unittest

from starrupture_api.utils import clean_number


class UtilsTests(unittest.TestCase):
    def test_clean_number(self) -> None:
        self.assertEqual(clean_number(4.0), 4)
        self.assertEqual(clean_number(1.23456789), 1.234568)


if __name__ == "__main__":
    unittest.main()
