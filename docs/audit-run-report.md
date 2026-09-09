# Audit validation run 34331141642

Commit tested: c9302cfe6c7a343bc2161aab07248d4292fbc83d

Apply: success; Python: success; Debug: failure; Release: failure.

CUDA-tagged hardware tests are excluded on this hosted VM. No production OCR, secrets, deployment or image folders are used.

## apply.log

~~~~text
Reviewed changed paths: OcrLineTool.App/RuleEngine.cs, OcrLineTool.Tests/AuditSafetyRegressionTests.cs, OcrLineTool.Tests/SixCardRegressionTests.cs, OcrLineTool.Tests/TemplateSelectionTests.cs, OcrLineTool.Tests/VisualTemplateMatcherTests.cs, docs/audit-phase1-applied.md
~~~~

## debug.log

~~~~text
   at System.Lazy`1.CreateValue()
   at OcrLineTool.CredentialSchedule.ForSlot(Int32 slot) in D:\a\NS\NS\OcrLineTool.App\CredentialSchedule.cs:line 57
   at OcrLineTool.CredentialSchedule.ForDate(DateOnly date) in D:\a\NS\NS\OcrLineTool.App\CredentialSchedule.cs:line 87
   at OcrLineTool.MainForm.RefreshCredentialLabel() in D:\a\NS\NS\OcrLineTool.App\MainForm.cs:line 644
   at OcrLineTool.MainForm..ctor(String imageDirectory, Action`1 openFile) in D:\a\NS\NS\OcrLineTool.App\MainForm.cs:line 177
   at OcrLineTool.MainForm..ctor() in D:\a\NS\NS\OcrLineTool.App\MainForm.cs:line 116
   at OcrLineTool.Tests.MainFormTests.ShowsASelectableFolderListWithoutTheRemovedChooseButton() in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 401
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed OcrLineTool.Tests.MainFormTests.OpensOnlyTheSelectedGroupsCurrentIssueTextAndNeverCreatesMissingResults [11 ms]
  Error Message:
   OcrLineTool.OcrException : 未找到 OCR 密钥配置文件：C:\Users\Administrator\Desktop\每天工具\ocrKey\secrets.json
  Stack Trace:
     at OcrLineTool.OcrSecretsLoader.Load(String path) in D:\a\NS\NS\OcrLineTool.App\OcrSecretsLoader.cs:line 42
   at OcrLineTool.CredentialSchedule.<>c.<.cctor>b__16_0() in D:\a\NS\NS\OcrLineTool.App\CredentialSchedule.cs:line 35
   at System.Lazy`1.ViaFactory(LazyThreadSafetyMode mode)
--- End of stack trace from previous location ---
   at System.Lazy`1.CreateValue()
   at OcrLineTool.CredentialSchedule.ForSlot(Int32 slot) in D:\a\NS\NS\OcrLineTool.App\CredentialSchedule.cs:line 57
   at OcrLineTool.CredentialSchedule.ForDate(DateOnly date) in D:\a\NS\NS\OcrLineTool.App\CredentialSchedule.cs:line 87
   at OcrLineTool.MainForm.RefreshCredentialLabel() in D:\a\NS\NS\OcrLineTool.App\MainForm.cs:line 644
   at OcrLineTool.MainForm..ctor(String imageDirectory, Action`1 openFile) in D:\a\NS\NS\OcrLineTool.App\MainForm.cs:line 177
   at InvokeStub_MainForm..ctor(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
[xUnit.net 00:00:02.10]     OcrLineTool.Tests.RuleEngineTests.UsesFormulaFolderAsIdentityWhenFormulaTitleIsOmitted [FAIL]
  Failed OcrLineTool.Tests.RuleEngineTests.UsesFormulaFolderAsIdentityWhenFormulaTitleIsOmitted [6 ms]
  Error Message:
   Assert.Equal() Failure: Collections differ
                                                          ↓ (pos 1)
Expected: <generated>                          ["公式杀两肖肖", "公式杀两尾尾"]
Actual:   SelectArrayIterator<OcrRule, string> ["公式杀两肖肖"]
  Stack Trace:
     at OcrLineTool.Tests.RuleEngineTests.UsesFormulaFolderAsIdentityWhenFormulaTitleIsOmitted() in D:\a\NS\NS\OcrLineTool.Tests\RuleEngineTests.cs:line 401
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
[xUnit.net 00:00:02.58]     OcrLineTool.Tests.JieshaoTests.ExtractsAllFourJieshaoOutputsFromOneCard [FAIL]
[xUnit.net 00:00:02.60]     OcrLineTool.Tests.JieshaoTests.FreshPrimaryRetryResponseUpdatesValuesBeforeFallback(label: "杰少九肖", expected: "龙猴羊蛇虎兔鸡马狗") [FAIL]
[xUnit.net 00:00:02.62]     OcrLineTool.Tests.JieshaoTests.FreshPrimaryRetryResponseUpdatesValuesBeforeFallback(label: "杰少杀一尾", expected: "0尾") [FAIL]
  Failed OcrLineTool.Tests.JieshaoTests.ExtractsAllFourJieshaoOutputsFromOneCard [2 ms]
  Error Message:
   Assert.Equal() Failure: Strings differ
Expected: "鼠"
Actual:   null
  Stack Trace:
     at OcrLineTool.Tests.JieshaoTests.ExtractsAllFourJieshaoOutputsFromOneCard() in D:\a\NS\NS\OcrLineTool.Tests\JieshaoTests.cs:line 239
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed OcrLineTool.Tests.JieshaoTests.FreshPrimaryRetryResponseUpdatesValuesBeforeFallback(label: "杰少九肖", expected: "龙猴羊蛇虎兔鸡马狗") [15 ms]
  Error Message:
   OcrLineTool.OcrException : 未找到 OCR 密钥配置文件：C:\Users\Administrator\Desktop\每天工具\ocrKey\secrets.json
  Stack Trace:
     at OcrLineTool.OcrSecretsLoader.Load(String path) in D:\a\NS\NS\OcrLineTool.App\OcrSecretsLoader.cs:line 42
   at OcrLineTool.CredentialSchedule.<>c.<.cctor>b__16_0() in D:\a\NS\NS\OcrLineTool.App\CredentialSchedule.cs:line 35
   at System.Lazy`1.ViaFactory(LazyThreadSafetyMode mode)
--- End of stack trace from previous location ---
   at System.Lazy`1.CreateValue()
   at OcrLineTool.CredentialSchedule.ForSlot(Int32 slot) in D:\a\NS\NS\OcrLineTool.App\CredentialSchedule.cs:line 57
   at OcrLineTool.CredentialSchedule.ForDate(DateOnly date) in D:\a\NS\NS\OcrLineTool.App\CredentialSchedule.cs:line 87
   at OcrLineTool.MainForm.RefreshCredentialLabel() in D:\a\NS\NS\OcrLineTool.App\MainForm.cs:line 644
   at OcrLineTool.MainForm..ctor(String imageDirectory, Action`1 openFile) in D:\a\NS\NS\OcrLineTool.App\MainForm.cs:line 177
   at InvokeStub_MainForm..ctor(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
  Failed OcrLineTool.Tests.JieshaoTests.FreshPrimaryRetryResponseUpdatesValuesBeforeFallback(label: "杰少杀一尾", expected: "0尾") [13 ms]
  Error Message:
   OcrLineTool.OcrException : 未找到 OCR 密钥配置文件：C:\Users\Administrator\Desktop\每天工具\ocrKey\secrets.json
  Stack Trace:
     at OcrLineTool.OcrSecretsLoader.Load(String path) in D:\a\NS\NS\OcrLineTool.App\OcrSecretsLoader.cs:line 42
   at OcrLineTool.CredentialSchedule.<>c.<.cctor>b__16_0() in D:\a\NS\NS\OcrLineTool.App\CredentialSchedule.cs:line 35
   at System.Lazy`1.ViaFactory(LazyThreadSafetyMode mode)
--- End of stack trace from previous location ---
   at System.Lazy`1.CreateValue()
   at OcrLineTool.CredentialSchedule.ForSlot(Int32 slot) in D:\a\NS\NS\OcrLineTool.App\CredentialSchedule.cs:line 57
   at OcrLineTool.CredentialSchedule.ForDate(DateOnly date) in D:\a\NS\NS\OcrLineTool.App\CredentialSchedule.cs:line 87
   at OcrLineTool.MainForm.RefreshCredentialLabel() in D:\a\NS\NS\OcrLineTool.App\MainForm.cs:line 644
   at OcrLineTool.MainForm..ctor(String imageDirectory, Action`1 openFile) in D:\a\NS\NS\OcrLineTool.App\MainForm.cs:line 177
   at InvokeStub_MainForm..ctor(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
[xUnit.net 00:00:02.74]     OcrLineTool.Tests.TemplateSelectionTests.PartialFirstRunKeepsMatchedCandidateAndLeavesOtherRuleMissing [FAIL]
[xUnit.net 00:00:02.76]     OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "missing-file") [FAIL]
[xUnit.net 00:00:02.79]     OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "wrong-rule-ids") [FAIL]
  Failed OcrLineTool.Tests.TemplateSelectionTests.PartialFirstRunKeepsMatchedCandidateAndLeavesOtherRuleMissing [30 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.PartialFirstRunKeepsMatchedCandidateAndLeavesOtherRuleMissing() in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 36
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "missing-file") [26 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(String failure) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 60
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "wrong-rule-ids") [24 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(String failure) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 60
--- End of stack trace from previous location ---
[xUnit.net 00:00:02.82]     OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "invalid-json") [FAIL]
[xUnit.net 00:00:02.87]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: False) [FAIL]
[xUnit.net 00:00:02.90]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: True) [FAIL]
[xUnit.net 00:00:02.93]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: False) [FAIL]
[xUnit.net 00:00:02.95]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: True) [FAIL]
  Failed OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "invalid-json") [29 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(String failure) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 60
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: False) [52 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: True) [28 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: False) [23 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: True) [22 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
Results File: D:\a\NS\NS\audit-test-results\debug.trx

Failed!  - Failed:    55, Passed:   753, Skipped:     0, Total:   808, Duration: 2 s - OcrLineTool.Tests.dll (net8.0)
~~~~

## python.log

~~~~text
OCR_PROGRESS|1|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-l1ed_gta\杰少\sample.png
OCR_PROGRESS|2|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-l1ed_gta\其他\sample.png
OCR_PROGRESS|1|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-xwyk5m5v\杰少\sample.png
OCR_PROGRESS|2|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-xwyk5m5v\其他\sample.png
............
----------------------------------------------------------------------
Ran 12 tests in 0.185s

OK
~~~~

## release.log

~~~~text
   at OcrLineTool.CredentialSchedule.ForDate(DateOnly date)
   at OcrLineTool.MainForm.RefreshCredentialLabel()
   at OcrLineTool.MainForm..ctor(String imageDirectory, Action`1 openFile)
   at InvokeStub_MainForm..ctor(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
  Failed OcrLineTool.Tests.JieshaoTests.ExtractsAllFourJieshaoOutputsFromOneCard [2 ms]
  Error Message:
   Assert.Equal() Failure: Strings differ
Expected: "鼠"
Actual:   null
  Stack Trace:
     at OcrLineTool.Tests.JieshaoTests.ExtractsAllFourJieshaoOutputsFromOneCard() in D:\a\NS\NS\OcrLineTool.Tests\JieshaoTests.cs:line 239
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed OcrLineTool.Tests.JieshaoTests.FreshPrimaryRetryResponseUpdatesValuesBeforeFallback(label: "杰少九肖", expected: "龙猴羊蛇虎兔鸡马狗") [14 ms]
  Error Message:
   OcrLineTool.OcrException : 未找到 OCR 密钥配置文件：C:\Users\Administrator\Desktop\每天工具\ocrKey\secrets.json
  Stack Trace:
     at OcrLineTool.OcrSecretsLoader.Load(String path)
   at OcrLineTool.CredentialSchedule.<>c.<.cctor>b__16_0()
   at System.Lazy`1.ViaFactory(LazyThreadSafetyMode mode)
--- End of stack trace from previous location ---
   at System.Lazy`1.CreateValue()
   at OcrLineTool.CredentialSchedule.ForSlot(Int32 slot)
   at OcrLineTool.CredentialSchedule.ForDate(DateOnly date)
   at OcrLineTool.MainForm.RefreshCredentialLabel()
   at OcrLineTool.MainForm..ctor(String imageDirectory, Action`1 openFile)
   at InvokeStub_MainForm..ctor(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
  Failed OcrLineTool.Tests.JieshaoTests.FreshPrimaryRetryResponseUpdatesValuesBeforeFallback(label: "杰少杀一尾", expected: "0尾") [18 ms]
  Error Message:
   OcrLineTool.OcrException : 未找到 OCR 密钥配置文件：C:\Users\Administrator\Desktop\每天工具\ocrKey\secrets.json
  Stack Trace:
     at OcrLineTool.OcrSecretsLoader.Load(String path)
   at OcrLineTool.CredentialSchedule.<>c.<.cctor>b__16_0()
   at System.Lazy`1.ViaFactory(LazyThreadSafetyMode mode)
--- End of stack trace from previous location ---
   at System.Lazy`1.CreateValue()
   at OcrLineTool.CredentialSchedule.ForSlot(Int32 slot)
   at OcrLineTool.CredentialSchedule.ForDate(DateOnly date)
   at OcrLineTool.MainForm.RefreshCredentialLabel()
   at OcrLineTool.MainForm..ctor(String imageDirectory, Action`1 openFile)
   at InvokeStub_MainForm..ctor(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
  Failed OcrLineTool.Tests.PublishConfigurationTests.CopiesCudaModelsFromTheSiblingModelDirectory [< 1 ms]
  Error Message:
   Assert.True() Failure
Expected: True
Actual:   False
  Stack Trace:
     at OcrLineTool.Tests.PublishConfigurationTests.CopiesCudaModelsFromTheSiblingModelDirectory() in D:\a\NS\NS\OcrLineTool.Tests\PublishConfigurationTests.cs:line 36
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
[xUnit.net 00:00:01.68]     OcrLineTool.Tests.MainFormTests.UsesAThreeColumnWorkspaceForSettingsPreviewAndResults [FAIL]
[xUnit.net 00:00:01.70]     OcrLineTool.Tests.MainFormTests.AutomaticFolderRefreshKeepsTheOldListDuringATransientRootFailure [FAIL]
[xUnit.net 00:00:01.71]     OcrLineTool.Tests.MainFormTests.ClickingMissingSummaryWritesAReportWithoutReplacingRecognitionResults(failToOpen: False) [FAIL]
[xUnit.net 00:00:01.75]     OcrLineTool.Tests.MainFormTests.ClickingMissingSummaryWritesAReportWithoutReplacingRecognitionResults(failToOpen: True) [FAIL]
[xUnit.net 00:00:01.76]     OcrLineTool.Tests.MainFormTests.MissingSummaryAndClearButtonsFitAtMinimumWindowSize(scale: 1) [FAIL]
[xUnit.net 00:00:01.77]     OcrLineTool.Tests.MainFormTests.MissingSummaryAndClearButtonsFitAtMinimumWindowSize(scale: 1.35416698) [FAIL]
[xUnit.net 00:00:01.78]     OcrLineTool.Tests.MainFormTests.EnablesManualRetryForMissingResultsFromAnyGroupAtTheCurrentIssue [FAIL]
[xUnit.net 00:00:01.80]     OcrLineTool.Tests.MainFormTests.HasADisabledManualDistributionButtonUntilASelectedGroupResultExists [FAIL]
[xUnit.net 00:00:01.82]     OcrLineTool.Tests.MainFormTests.EnablesManualRetryWhenAnExistingGroupResultContainsMissingItems [FAIL]
[xUnit.net 00:00:01.84]     OcrLineTool.Tests.MainFormTests.HasADisabledRetryMissingButtonUntilAGroupFinishesWithMissingItems [FAIL]
[xUnit.net 00:00:01.86]     OcrLineTool.Tests.MainFormTests.ManualCredentialListIncludesDSlots [FAIL]
[xUnit.net 00:00:01.87]     OcrLineTool.Tests.MainFormTests.UsesDarkSurfacesWithReadableResultText [FAIL]
[xUnit.net 00:00:01.89]     OcrLineTool.Tests.MainFormTests.SizesTheToolsCardToItsContentInsteadOfStretchingIt [FAIL]
[xUnit.net 00:00:01.90]     OcrLineTool.Tests.MainFormTests.AutomaticFolderRefreshRecoversWhenRootAppearsWithoutSelectingANewGroup [FAIL]
[xUnit.net 00:00:01.91]     OcrLineTool.Tests.MainFormTests.MissingSummaryButtonIsAvailableWithoutSelectingAGroupAndDisabledWhileBusy [FAIL]
[xUnit.net 00:00:01.93]     OcrLineTool.Tests.MainFormTests.WindowTitleIncludesTheApplicationVersion [FAIL]
[xUnit.net 00:00:01.94]     OcrLineTool.Tests.MainFormTests.ProvidesSeparateLocalPrimaryRecognitionButtonWithoutReplacingCurrentMode [FAIL]
[xUnit.net 00:00:01.95]     OcrLineTool.Tests.YanranConfirmedHardeningTests.RestoresTheCurrentDoomsdayRowFromItsAnchoredConsecutiveHistory [FAIL]
[xUnit.net 00:00:01.95]     OcrLineTool.Tests.MainFormTests.ShowsASelectableFolderListWithoutTheRemovedChooseButton [FAIL]
[xUnit.net 00:00:01.98]     OcrLineTool.Tests.MainFormTests.OpensOnlyTheSelectedGroupsCurrentIssueTextAndNeverCreatesMissingResults [FAIL]
[xUnit.net 00:00:02.26]     OcrLineTool.Tests.JieshaoTests.ExtractsAllFourJieshaoOutputsFromOneCard [FAIL]
[xUnit.net 00:00:02.29]     OcrLineTool.Tests.JieshaoTests.FreshPrimaryRetryResponseUpdatesValuesBeforeFallback(label: "杰少九肖", expected: "龙猴羊蛇虎兔鸡马狗") [FAIL]
[xUnit.net 00:00:02.31]     OcrLineTool.Tests.JieshaoTests.FreshPrimaryRetryResponseUpdatesValuesBeforeFallback(label: "杰少杀一尾", expected: "0尾") [FAIL]
[xUnit.net 00:00:02.33]     OcrLineTool.Tests.PublishConfigurationTests.CopiesCudaModelsFromTheSiblingModelDirectory [FAIL]
[xUnit.net 00:00:02.92]     OcrLineTool.Tests.TemplateSelectionTests.PartialFirstRunKeepsMatchedCandidateAndLeavesOtherRuleMissing [FAIL]
[xUnit.net 00:00:02.95]     OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "missing-file") [FAIL]
  Failed OcrLineTool.Tests.TemplateSelectionTests.PartialFirstRunKeepsMatchedCandidateAndLeavesOtherRuleMissing [34 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.PartialFirstRunKeepsMatchedCandidateAndLeavesOtherRuleMissing() in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 36
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "missing-file") [32 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(String failure) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 60
--- End of stack trace from previous location ---
[xUnit.net 00:00:02.97]     OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "wrong-rule-ids") [FAIL]
[xUnit.net 00:00:03.00]     OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "invalid-json") [FAIL]
[xUnit.net 00:00:03.02]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: False) [FAIL]
[xUnit.net 00:00:03.05]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: True) [FAIL]
[xUnit.net 00:00:03.07]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: False) [FAIL]
[xUnit.net 00:00:03.09]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: True) [FAIL]
  Failed OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "wrong-rule-ids") [24 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(String failure) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 60
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "invalid-json") [23 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(String failure) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 60
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: False) [22 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: True) [28 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: False) [21 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: True) [23 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
Results File: D:\a\NS\NS\audit-test-results\release.trx

Failed!  - Failed:    55, Passed:   753, Skipped:     0, Total:   808, Duration: 2 s - OcrLineTool.Tests.dll (net8.0)
~~~~

