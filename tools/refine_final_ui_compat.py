from pathlib import Path

path = Path('OcrLineTool.App/MainForm.cs')
text = path.read_text(encoding='utf-8')
old = '''    private bool CanManualDistribute() =>
        selectedImageDirectory is not null
        && File.Exists(ResultFilePaths.ForGroup(AppContext.BaseDirectory, selectedImageDirectory, Decimal.ToInt32(issueInput.Value)))
        && File.Exists(ResultFilePaths.ForRecognitionState(AppContext.BaseDirectory, selectedImageDirectory, Decimal.ToInt32(issueInput.Value)));'''
new = '''    private bool CanManualDistribute() =>
        selectedImageDirectory is not null
        && File.Exists(ResultFilePaths.ForGroup(AppContext.BaseDirectory, selectedImageDirectory, Decimal.ToInt32(issueInput.Value)));'''
if text.count(old) != 1:
    raise RuntimeError(f'expected one manual-button anchor, found {text.count(old)}')
path.write_text(text.replace(old, new, 1), encoding='utf-8')
print('Preserved manual-distribution button UI; trusted-state enforcement remains inside the action')
