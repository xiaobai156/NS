# 项目结构

## 开发目录

- `OcrLineTool.App/`：WinForms OCR 应用源码、规则配置和发布项目。
- `OcrLineTool.Tests/`：自动化测试项目。
- `tools/legacy-tests/`：历史批处理、修复和冒烟测试工具，不参与主应用构建。
- `coverage.runsettings`：测试覆盖率配置。
- `AGENTS.md`：项目协作和操作约束。

## 归档目录

- `archive/backups/`：历史结果备份。
- `archive/publish-history/`：旧版本发布文件。
- `archive/test-results/`：旧版本测试结果。

## 实际使用输出

`outputs/` 是给日常使用的目录。正式 EXE 和 ZIP 直接放在首页，运行配置和结果放在子目录：

- `OCR整行提取工具.exe`
- `OCR整行提取工具-v版本号.zip`
- `配置文件/`
- `运行组件/`
- `临时文件/`
- `重要结果/`

EXE 旁边的 `.dll`、`.deps.json` 和 `.runtimeconfig.json` 是运行必需文件，不能移走。
