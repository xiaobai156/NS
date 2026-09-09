import importlib.util
from pathlib import Path
import unittest


SOURCE = Path(__file__).resolve().parents[1] / 'OcrLineTool.App' / 'cuda_template.py'
spec = importlib.util.spec_from_file_location('cuda_template', SOURCE)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CudaTemplateTests(unittest.TestCase):
    def test_missing_cuda_is_fatal(self):
        class NoCuda:
            @staticmethod
            def getCudaEnabledDeviceCount():
                return 0
        class Cv:
            cuda = NoCuda()
        with self.assertRaisesRegex(RuntimeError, 'CUDA'):
            module.require_cuda(Cv())

    def test_region_uses_even_rounding_and_clamps_to_image(self):
        self.assertEqual((2, 4), module.region_bounds(10, 10, 0.25, 0.45, 0))
        self.assertEqual((0, 1), module.region_bounds(10, 10, 0, 0.1, -0.04))
        self.assertEqual((9, 10), module.region_bounds(10, 10, 1, 2, 0))

    def test_invalid_regions_are_rejected(self):
        for top, bottom in [(0.2, 0.1), (-1, 1), (0, float('nan'))]:
            with self.assertRaises(ValueError):
                module.region_bounds(100, 100, top, bottom, 0)


if __name__ == '__main__':
    unittest.main()
