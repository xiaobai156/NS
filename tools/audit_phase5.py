from apply_audit_fixes import ROOT, MANIFEST, changed, write, replace
import hashlib
import json
import shutil
import subprocess

if MANIFEST.exists():
    changed.extend(json.loads(MANIFEST.read_text(encoding='utf-8')))

if not (ROOT / 'docs/audit-phase5-applied.md').exists():
    # Preserve the exact existing CUDA DLL before untracking build output copies.
    binaries = sorted((ROOT / 'OcrLineTool.App/bin').rglob('cuda_hash.dll'))
    if not binaries:
        raise RuntimeError('Audited native DLL not found; do not remove generated outputs before preserving it.')
    source = binaries[0]
    destination = ROOT / 'native/win-x64/cuda_hash.dll'
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, destination)
    changed.append(destination.relative_to(ROOT).as_posix())
    digest = hashlib.sha256(destination.read_bytes()).hexdigest()
    for path in ('OcrLineTool.App/OcrLineTool.App.csproj', 'OcrLineTool.Tests/OcrLineTool.Tests.csproj'):
        text = (ROOT / path).read_text(encoding='utf-8-sig')
        text = replace(text, r'..\artifacts\cuda-validation\cuda_hash.dll', r'..\native\win-x64\cuda_hash.dll')
        write(path, text)
    tracked = subprocess.check_output(['git', 'ls-files', '-z'], cwd=ROOT).decode('utf-8').split('\0')
    generated = [path for path in tracked if path and any(part in {'bin', 'obj', '__pycache__', 'TestResults'} for part in path.split('/'))]
    (ROOT / '.audit-remove.json').write_text(json.dumps(generated, ensure_ascii=False), encoding='utf-8')
    ignore = (ROOT / '.gitignore').read_text(encoding='utf-8-sig')
    write('.gitignore', ignore.rstrip() + '\n\n# Build/test state is never a source of truth.\n**/bin/\n**/obj/\n**/__pycache__/\n**/TestResults/\n*.pyc\naudit-test-results/\n.audit-changed.json\n.audit-remove.json\n')
    write('native/README.md', '# Native CUDA dependency\n\n'
        'The Windows x64 `cuda_hash.dll` is retained byte-for-byte from the audited repository, '
        'not silently rebuilt or downloaded. Both projects now reference this explicit path instead of a temporary validation directory.\n\n'
        f'SHA-256: `{digest}`\n\n'
        'This change verifies packaging identity only. It does not claim CUDA execution, driver compatibility, '
        'model accuracy, or native ABI validation on a GPU. Keep the binary and native source changes reviewed together; '
        'a future native rebuild must use the CUDA/Visual C++ toolchain and run the CUDA-tagged tests.\n')
    # Use a portable path expression in the text-only candidate test.
    path = 'OcrLineTool.Tests/AuditPipelineRegressionTests.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    lines = text.splitlines()
    for index, line in enumerate(lines):
        if 'string image = Path.Combine(' in line:
            lines[index] = '        string image = Path.Combine(Path.GetTempPath(), "结果", "嫣然心水", "小苹果", "当前.jpg");'
    write(path, '\n'.join(lines) + '\n')
    write('OcrLineTool.Tests/audit_model_cache_test.py', '''import importlib.util
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[1] / "OcrLineTool.App" / "paddle_local_ocr.py"
spec = importlib.util.spec_from_file_location("audit_model_worker", SCRIPT)
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class ModelCacheAuditTests(unittest.TestCase):
    def create_model(self, path, payload):
        path.mkdir(parents=True, exist_ok=True)
        (path / "inference.json").write_text("{}", encoding="utf-8")
        (path / "inference.pdiparams").write_bytes(payload)

    def test_updated_weights_get_a_different_ascii_cache_root(self):
        with tempfile.TemporaryDirectory(prefix="ns-model-audit-") as temp:
            root = Path(temp)
            source = root / "源模型"
            name = "PP-OCRv6_small_det"
            self.create_model(source / name, b"old")
            with patch.dict(os.environ, {"OCR_NVIDIA_MODEL_DIR": str(source), "OCR_NVIDIA_MODEL_CACHE": str(root / "cache")}):
                first = worker._prepare_model_root((name,))
                (source / name / "inference.pdiparams").write_bytes(b"new")
                second = worker._prepare_model_root((name,))
            self.assertNotEqual(first, second)
            self.assertEqual(b"old", (first / name / "inference.pdiparams").read_bytes())
            self.assertEqual(b"new", (second / name / "inference.pdiparams").read_bytes())

    def test_existing_target_is_not_accepted_just_because_files_exist(self):
        with tempfile.TemporaryDirectory(prefix="ns-model-audit-") as temp:
            root = Path(temp)
            self.create_model(root / "source", b"new")
            self.create_model(root / "target", b"old")
            worker._stage_model(root / "source", root / "target")
            self.assertEqual(b"new", (root / "target" / "inference.pdiparams").read_bytes())


if __name__ == "__main__":
    unittest.main()
''')
    path = 'AGENTS.md'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text += '''

## 审查修复后的安全边界（优先于旧实现描述）

- 期号必须来自图片/OCR 的明确证据；不得用目标期数修复上一期后缀，不得给八个尾数自动补 0。
- 同期不同可信值保留为冲突，不使用先到先得；冲突和不完整项不得自动分流。
- small 用于候选身份筛选；已确定身份的难图不能仅因 small 未读出业务值而丢弃。最终仍按原来的云主识别/medium 本地主识别分工。
- 活动会话期间日期刷新不能改变期数或重建重试状态；开始识别时重新扫描所选目录。
- 缓存必须验证图像内容和处理身份。无效缓存不能阻断手动复抓。模型权重和 worker 源码更新应使对应本地缓存失效。
- 云账号显示不加载密钥；允许部分槽位未配置，在真正请求时解析凭据。禁止将生产密钥、OCR 请求或生产图片用于测试。
- 用户可按 Esc 取消任务；只终止当前任务创建的进程。关闭窗口时先取消并等待任务退出。
- 分流仅自动修正本工具有明确归属记录的行；未知归属的冲突保留原文件并报告。共享目标的其他程序应遵守相同写入锁协议。
- 主结果与诊断采用同目录临时文件替换并保留最近备份；目标与归属记录不是跨文件原子事务，失败不得伪报全部成功。
- 托管 Windows CI 不具备 CUDA GPU。标记 Category=CUDA 的硬件测试必须另在 N 卡环境执行，不能用 CPU OCR 兜底替代。
- native/win-x64/cuda_hash.dll 为明确保留的原生依赖；bin/obj/TestResults/__pycache__ 不再作为源码入库。
- 本轮只修改源码、测试和构建引用；不升级应用版本、不 publish、不部署、不替换实际运行 EXE。
'''
    write(path, text)
    write('docs/audit-phase5-applied.md', '# Audit repair phase 5\n\n'
        'Retained the existing native DLL in an explicit dependency directory with a SHA-256 identity, '
        'updated project references, and removed generated build/test caches from the index. '
        'Added model-cache regressions that require neither models nor CUDA. '
        'The application version and deployed EXE are unchanged.\n')

MANIFEST.write_text(json.dumps(changed, ensure_ascii=False), encoding='utf-8')
