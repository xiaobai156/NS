# v1.5.242 独立复审

结论：通过。此次源码及测试变更没有尚未解决的阻断问题，可以继续已授权的打包与推送；发布物验证仍须由主线程完成。

审核时间：2026-11-07（Asia/Shanghai）。基线 HEAD：`3bbcb870`。审核包含全部当前 tracked diff，以及新增的 `RuleEngine.RuiErTeam.cs`、`RuiErTeamRecovery.cs`、两组测试及 fixture、版本说明和验收文档。遵守 AGENTS.md；审核者仅只读检视和调用现有 Release DLL，未修改源码、未构建、未请求云 OCR、未触碰正式 TXT、未提交或打包。本报告是审核者唯一写入文件。

变更效果：蕊儿团队按同一视图/区域的本人卡或新澳统计表本人行取值；medium 补读保留真实原图和裁剪证据。绿杀及 11 项地域两肖要求完整身份，单项复抓、目录和摘要不能绕过。

## 审核中发现并已修复的问题

| 原风险 | 实证与当前结果 | 最终代码位置 |
| --- | --- | --- |
| P1：跨区域拼成完整身份 | `广东`、U+001E、`两肖必杀`、`310期广西报:羊牛` 原先命中广东并取羊牛；最新 DLL 两个候选入口均拒绝。 | `RuleEngine.cs:548` |
| P1：下一作者小字号/括号标题未截断 | 安之若素残缺行，下方蕊儿 logo 漏读、标题高度20或标题为【蕊儿】，原先取得蕊儿的蛇；最新 DLL 均 Missing。括号仅合法业务 payload 可豁免边界；纯文本旧入口也统一该约束，蕊儿、【蕊儿】、[蕊儿]三种断区均独立复验为 Missing。 | `RuleEngine.RuiErTeam.cs:40`、`:88`、`RuleEngine.cs:3057` |
| P1：低置信或残缺统计标题失去边界 | 310新澳标题下出现低置信309新澳/旧澳标题，或只有旧澳统计，原先将后续本人行归给310；最新 DLL 均 Missing。边界和授权使用不同条件。 | `RuleEngine.RuiErTeam.cs:51` |
| P1：腾讯置信度量纲错误 | `retry/tencent`业务行 Confidence=2（2%）原先通过0.8阈值并成功；最新 DLL Missing。统一复用提供方置信度校验，同时拒绝非有限值和超范围 Paddle 分数。 | `RuleEngine.RuiErTeam.cs:7` |
| P2：云优先复抓写入 medium 最终值 | `retryUsesLocalOcr=false` 原先仍在云后触发新增本地本人区域补读。当前云后入口排除该类规则；本地主识别/本机优先仍在云前补读。 | `MainForm.cs:3181` |
| P1：多组括号只取首个答案 | 同行 `310期杀①肖【龙+7尾】【蛇+1尾】` 原先 Success龙；最新 DLL Missing。坐标与纯文本共用唯一闭合 payload 检查。 | `RuleEngine.cs:3096` |
| P1：丢弃本人已有计数后借右邻字段 | 本人 token `【安之若素05:01】`，右邻 `05:00】蛇` 原先 Success蛇；最新 DLL Missing。姓名、已有计数和右侧字段统一整格校验；右侧仅合法续写生肖仍 Success。 | `RuleEngine.RuiErTeam.cs:141`、`:183` |

以上问题均由审核者调用真实已编译入口复现；主线程修复和补回归后，审核者再次对更新 DLL 独立复验。没有把 OCR 缺失当错误业务值，也没有采用此前已纠正的“安之若素310虎”判断；实际为龙＋7尾。

## 验证证据

- 审核者独立加载最终 Release DLL，逐一复验上述提取反例；现均 Missing 或 0 候选，合法 summary 续写仍成功。
- 审核者独立读取新澳六合彩资料 1035 张已保存的真实 medium OCR 证据，调用路径候选入口并逐项对照12项归属：1035张、12候选、0异常。主线程 `identity-validation.json` 同时记录23→12候选、移除11个误候选、12个模板值验证。
- 审核者独立重放 `ruier-owned-310.json`：14个字段全部正确，包含安之若素龙/7尾、梓涵12合/4头单。紫琼女神两字段缺可靠身份，保持 Missing。
- 已阅读最新 `gpu-reviewed-final.log`、`latest-gpu-observations.json`、`validation.json`：真实GPU 14个裁剪重推成功，14正确、14保存后重载一致、0云调用。其后修改为统计整格解析和纯文本边界校验，未改变裁剪几何或GPU调用；已用同批真实证据重新回放。
- 已读取最终 `release-acceptance.log` 与 `coverage-reviewed-final.log`：Debug/Release各2370通过、17跳过、0失败；行覆盖率83.30%，80%门槛通过。
- `git diff --check` 通过，仅既有换行符提示。
- 审核时最终 Release DLL SHA-256：`F76E657DB6BEBCC62F7A33DEC60C9B386C003A4F8281BD7E4165494B85F2AE6C`。

还检查了候选入口、模板最终提取、各恢复阶段、GPU异常传播、SourceHash/InputHash固定与保存、ExtractorRevision=19、ResultValues sticky conflict和旧状态恢复。此次身份约束没有被加到模板裁剪最终值上；已成立的冲突不会被后续成功覆盖。

## 未验证范围

审核者没有独立重启真实GPU或调用付费云，真实GPU结论来自本轮落盘证据并经独立解析回放；没有操作生产界面、执行正式分发或验收尚未生成的EXE/ZIP。未知新布局不在保证范围，证据不足继续 Missing。发布三份产物一致性、安装运行冒烟和推送结果由主线程后续验证。
