import importlib.util
import unittest
from pathlib import Path


class PaddleEvidenceTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        script = Path(__file__).parents[1] / "OcrLineTool.App" / "paddle_local_ocr.py"
        spec = importlib.util.spec_from_file_location("paddle_evidence_under_test", script)
        cls.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.module)

    def test_prediction_items_keep_text_confidence_box_and_view(self):
        first = {
            "rec_texts": ["251期", "鸡"],
            "rec_scores": [0.97, 0.83],
            "rec_boxes": [[10, 20, 110, 40], [15, 50, 35, 80]],
        }
        items = self.module._prediction_items(first, "original")
        self.assertEqual(["251期", "鸡"], [item["text"] for item in items])
        self.assertEqual([0.97, 0.83], [item["confidence"] for item in items])
        self.assertEqual([[10, 20, 100, 20], [15, 50, 20, 30]], [item["box"] for item in items])
        self.assertEqual(["original", "original"], [item["viewId"] for item in items])

    def test_polygon_boxes_are_normalized_without_ocr(self):
        polygon = [[10, 20], [110, 18], [112, 42], [8, 40]]
        self.assertEqual([8, 18, 104, 24], self.module._box_from(polygon))


if __name__ == "__main__":
    unittest.main()
