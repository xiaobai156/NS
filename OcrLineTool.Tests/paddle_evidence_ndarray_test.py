import importlib.util
import unittest
from pathlib import Path

import numpy as np


class PaddleEvidenceNdarrayTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        script = Path(__file__).parents[1] / "OcrLineTool.App" / "paddle_local_ocr.py"
        spec = importlib.util.spec_from_file_location("paddle_evidence_ndarray_under_test", script)
        cls.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.module)

    def test_numpy_scores_and_boxes_do_not_use_array_truthiness(self):
        first = {
            "rec_texts": np.array(["251期", "鸡"], dtype=object),
            "rec_scores": np.array([0.97, 0.83], dtype=float),
            "rec_boxes": np.array([[10, 20, 110, 40], [15, 50, 35, 80]], dtype=float),
        }
        items = self.module._prediction_items(first, "original")
        self.assertEqual(["251期", "鸡"], [item["text"] for item in items])
        self.assertEqual([[10, 20, 100, 20], [15, 50, 20, 30]], [item["box"] for item in items])

    def test_rec_polys_are_preferred_when_rec_boxes_are_absent(self):
        first = {
            "rec_texts": ["鸡"],
            "rec_scores": np.array([0.91]),
            "rec_polys": np.array([[[100, 20], [140, 20], [140, 50], [100, 50]]], dtype=float),
            "dt_polys": np.array([
                [[0, 0], [10, 0], [10, 10], [0, 10]],
                [[100, 20], [140, 20], [140, 50], [100, 50]],
            ], dtype=float),
        }
        item = self.module._prediction_items(first, "original")[0]
        self.assertEqual([100, 20, 40, 30], item["box"])

    def test_unaligned_dt_polys_are_not_assigned_to_filtered_text(self):
        first = {
            "rec_texts": ["鸡"],
            "rec_scores": [0.91],
            "dt_polys": np.array([
                [[0, 0], [10, 0], [10, 10], [0, 10]],
                [[100, 20], [140, 20], [140, 50], [100, 50]],
            ], dtype=float),
        }
        item = self.module._prediction_items(first, "original")[0]
        self.assertIsNone(item["box"])

    def test_empty_numpy_arrays_are_safe(self):
        first = {
            "rec_texts": np.array([], dtype=object),
            "rec_scores": np.array([], dtype=float),
            "rec_boxes": np.empty((0, 4), dtype=float),
        }
        self.assertEqual([], self.module._prediction_items(first, "original"))


if __name__ == "__main__":
    unittest.main()
