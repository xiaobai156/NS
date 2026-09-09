# Audit validation run 34331962368

Commit tested: b0eae875c9269e9593fd6c3e6798cbc6c2659213

Apply: success; Python: failure; Debug: failure; Release: failure.

CUDA-tagged hardware tests are excluded on this hosted VM. No production OCR, secrets, deployment or image folders are used.

## apply.log

~~~~text
Reviewed changed paths: 
~~~~

## debug.log

~~~~text
[29]: Item:  "C:\\Users\\runneradmin\\AppData\\Local\\Temp\\ocr-"···
      Error: Assert.StartsWith() Failure: String start does not match
             String:         "[{"SourceGroup":"\\u65B0\\u6FB3\\u516D\\u5408"···
             Expected start: "原有内容"
[30]: Item:  "C:\\Users\\runneradmin\\AppData\\Local\\Temp\\ocr-"···
      Error: Assert.StartsWith() Failure: String start does not match
             String:         ""
             Expected start: "原有内容"
[33]: Item:  "C:\\Users\\runneradmin\\AppData\\Local\\Temp\\ocr-"···
      Error: Assert.StartsWith() Failure: String start does not match
             String:         "[{"SourceGroup":"\\u65B0\\u6FB3\\u516D\\u5408"···
             Expected start: "原有内容"
[34]: Item:  "C:\\Users\\runneradmin\\AppData\\Local\\Temp\\ocr-"···
      Error: Assert.StartsWith() Failure: String start does not match
             String:         ""
             Expected start: "原有内容"
[39]: Item:  "C:\\Users\\runneradmin\\AppData\\Local\\Temp\\ocr-"···
      Error: Assert.StartsWith() Failure: String start does not match
             String:         "[{"SourceGroup":"\\u65B0\\u6FB3\\u516D\\u5408"···
             Expected start: "原有内容"
[40]: Item:  "C:\\Users\\runneradmin\\AppData\\Local\\Temp\\ocr-"···
      Error: Assert.StartsWith() Failure: String start does not match
             String:         ""
             Expected start: "原有内容"
  Stack Trace:
     at OcrLineTool.Tests.MacauRuleHardeningTests.ValidatedSamplesFlowThroughRealDistributionConfigsOnlyToTheSelectedIssue() in D:\a\NS\NS\OcrLineTool.Tests\MacauRuleHardeningTests.cs:line 342
--- End of stack trace from previous location ---
[xUnit.net 00:00:02.09]     OcrLineTool.Tests.YanranConfirmedHardeningTests.RestoresTheCurrentDoomsdayRowFromItsAnchoredConsecutiveHistory [FAIL]
  Failed OcrLineTool.Tests.YanranConfirmedHardeningTests.RestoresTheCurrentDoomsdayRowFromItsAnchoredConsecutiveHistory [3 ms]
  Error Message:
   Assert.Equal() Failure: Strings differ
Expected: "1段"
Actual:   null
  Stack Trace:
     at OcrLineTool.Tests.YanranConfirmedHardeningTests.RestoresTheCurrentDoomsdayRowFromItsAnchoredConsecutiveHistory() in D:\a\NS\NS\OcrLineTool.Tests\YanranConfirmedHardeningTests.cs:line 78
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
[xUnit.net 00:00:02.14]     OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [FAIL]
  Failed OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [17 ms]
  Error Message:
   folderList bounds {X=27,Y=379,Width=320,Height=1} should fit inside  client area {X=0,Y=0,Width=374,Height=291}.
  Stack Trace:
     at OcrLineTool.Tests.MainFormTests.AssertControlFitsItsParent(Control control) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 870
   at OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(Single scale) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 538
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span`1 copyOfArgs, BindingFlags invokeAttr)
[xUnit.net 00:00:02.29]     OcrLineTool.Tests.HongrenguanTests.AutomaticAndManualReplayUseTheSameRoutesWithoutDuplicates [FAIL]
  Failed OcrLineTool.Tests.HongrenguanTests.AutomaticAndManualReplayUseTheSameRoutesWithoutDuplicates [78 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 2
Actual:   8
  Stack Trace:
     at OcrLineTool.Tests.HongrenguanTests.AutomaticAndManualReplayUseTheSameRoutesWithoutDuplicates() in D:\a\NS\NS\OcrLineTool.Tests\HongrenguanTests.cs:line 88
--- End of stack trace from previous location ---
[xUnit.net 00:00:02.84]     OcrLineTool.Tests.OcrClientHttpTests.TencentRecognizeAsyncObservesCancellationAndMapsConnectionFailure [FAIL]
  Failed OcrLineTool.Tests.OcrClientHttpTests.TencentRecognizeAsyncObservesCancellationAndMapsConnectionFailure [10 ms]
  Error Message:
   Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(OcrLineTool.OcrException)
Actual:   typeof(System.Threading.Tasks.TaskCanceledException)
---- System.Threading.Tasks.TaskCanceledException : A task was canceled.
-------- System.Threading.Tasks.TaskCanceledException : A task was canceled.
  Stack Trace:
     at OcrLineTool.Tests.OcrClientHttpTests.TencentRecognizeAsyncObservesCancellationAndMapsConnectionFailure() in D:\a\NS\NS\OcrLineTool.Tests\OcrClientHttpTests.cs:line 119
--- End of stack trace from previous location ---
----- Inner Stack Trace -----
   at System.Net.Http.HttpClient.HandleFailure(Exception e, Boolean telemetryStarted, HttpResponseMessage response, CancellationTokenSource cts, CancellationToken cancellationToken, CancellationTokenSource pendingRequestsCts)
   at System.Net.Http.HttpClient.<SendAsync>g__Core|83_0(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationTokenSource cts, Boolean disposeCts, CancellationTokenSource pendingRequestsCts, CancellationToken originalCancellationToken)
   at OcrLineTool.TencentOcrClient.RecognizeAsync(String imagePath, CancellationToken cancellationToken) in D:\a\NS\NS\OcrLineTool.App\OcrClients.cs:line 136
   at OcrLineTool.Tests.OcrClientHttpTests.<>c__DisplayClass3_1.<<TencentRecognizeAsyncObservesCancellationAndMapsConnectionFailure>b__1>d.MoveNext() in D:\a\NS\NS\OcrLineTool.Tests\OcrClientHttpTests.cs:line 120
--- End of stack trace from previous location ---
----- Inner Stack Trace -----
   at OcrLineTool.Tests.OcrClientHttpTests.<>c__DisplayClass3_0.<<TencentRecognizeAsyncObservesCancellationAndMapsConnectionFailure>b__0>d.MoveNext() in D:\a\NS\NS\OcrLineTool.Tests\OcrClientHttpTests.cs:line 98
--- End of stack trace from previous location ---
   at OcrLineTool.Tests.OcrClientHttpTests.FakeHttpMessageHandler.SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) in D:\a\NS\NS\OcrLineTool.Tests\OcrClientHttpTests.cs:line 401
   at System.Net.Http.HttpClient.<SendAsync>g__Core|83_0(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationTokenSource cts, Boolean disposeCts, CancellationTokenSource pendingRequestsCts, CancellationToken originalCancellationToken)
[xUnit.net 00:00:02.92]     OcrLineTool.Tests.TemplateSelectionTests.PartialFirstRunKeepsMatchedCandidateAndLeavesOtherRuleMissing [FAIL]
[xUnit.net 00:00:02.95]     OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "missing-file") [FAIL]
[xUnit.net 00:00:02.98]     OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "wrong-rule-ids") [FAIL]
[xUnit.net 00:00:03.00]     OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "invalid-json") [FAIL]
[xUnit.net 00:00:03.02]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: False) [FAIL]
  Failed OcrLineTool.Tests.TemplateSelectionTests.PartialFirstRunKeepsMatchedCandidateAndLeavesOtherRuleMissing [20 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.PartialFirstRunKeepsMatchedCandidateAndLeavesOtherRuleMissing() in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 36
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "missing-file") [35 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(String failure) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 60
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "wrong-rule-ids") [20 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(String failure) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 60
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "invalid-json") [19 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(String failure) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 60
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: False) [20 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
[xUnit.net 00:00:03.04]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: True) [FAIL]
[xUnit.net 00:00:03.05]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: False) [FAIL]
[xUnit.net 00:00:03.07]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: True) [FAIL]
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: True) [19 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: False) [15 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: True) [17 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 453
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio) in D:\a\NS\NS\OcrLineTool.App\VisualTemplateMatcher.cs:line 127
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
Results File: D:\a\NS\NS\audit-test-results\debug.trx

Failed!  - Failed:    24, Passed:   799, Skipped:     0, Total:   823, Duration: 2 s - OcrLineTool.Tests.dll (net8.0)
~~~~

## python.log

~~~~text
OCR_PROGRESS|1|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-403743c3\杰少\sample.png
OCR_PROGRESS|2|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-403743c3\其他\sample.png
OCR_PROGRESS|1|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-o47qw6c5\杰少\sample.png
OCR_PROGRESS|2|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-o47qw6c5\其他\sample.png
............F.
======================================================================
FAIL: test_stages_non_ascii_models_to_ascii_cache (paddle_local_ocr_test.CompactFolderTest.test_stages_non_ascii_models_to_ascii_cache)
----------------------------------------------------------------------
Traceback (most recent call last):
  File "D:\a\NS\NS\OcrLineTool.Tests\paddle_local_ocr_test.py", line 167, in test_stages_non_ascii_models_to_ascii_cache
    self.assertEqual(cache, prepared)
AssertionError: Windo[77 chars]cache') != Windo[77 chars]cache/008be0942ba2d80801cc495eeeea6fb9ce63e0da[21 chars]a75')

----------------------------------------------------------------------
Ran 14 tests in 0.126s

FAILED (failures=1)
~~~~

## release.log

~~~~text
  Stack Trace:
     at OcrLineTool.Tests.MacauRuleHardeningTests.ValidatedSamplesFlowThroughRealDistributionConfigsOnlyToTheSelectedIssue() in D:\a\NS\NS\OcrLineTool.Tests\MacauRuleHardeningTests.cs:line 342
--- End of stack trace from previous location ---
[xUnit.net 00:00:01.71]     OcrLineTool.Tests.OcrClientHttpTests.TencentRecognizeAsyncObservesCancellationAndMapsConnectionFailure [FAIL]
  Failed OcrLineTool.Tests.OcrClientHttpTests.TencentRecognizeAsyncObservesCancellationAndMapsConnectionFailure [24 ms]
  Error Message:
   Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(OcrLineTool.OcrException)
Actual:   typeof(System.Threading.Tasks.TaskCanceledException)
---- System.Threading.Tasks.TaskCanceledException : A task was canceled.
-------- System.Threading.Tasks.TaskCanceledException : A task was canceled.
  Stack Trace:
     at OcrLineTool.Tests.OcrClientHttpTests.TencentRecognizeAsyncObservesCancellationAndMapsConnectionFailure() in D:\a\NS\NS\OcrLineTool.Tests\OcrClientHttpTests.cs:line 119
--- End of stack trace from previous location ---
----- Inner Stack Trace -----
   at System.Net.Http.HttpClient.HandleFailure(Exception e, Boolean telemetryStarted, HttpResponseMessage response, CancellationTokenSource cts, CancellationToken cancellationToken, CancellationTokenSource pendingRequestsCts)
   at System.Net.Http.HttpClient.<SendAsync>g__Core|83_0(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationTokenSource cts, Boolean disposeCts, CancellationTokenSource pendingRequestsCts, CancellationToken originalCancellationToken)
   at OcrLineTool.TencentOcrClient.RecognizeAsync(String imagePath, CancellationToken cancellationToken)
   at OcrLineTool.Tests.OcrClientHttpTests.<>c__DisplayClass3_1.<<TencentRecognizeAsyncObservesCancellationAndMapsConnectionFailure>b__1>d.MoveNext() in D:\a\NS\NS\OcrLineTool.Tests\OcrClientHttpTests.cs:line 120
--- End of stack trace from previous location ---
----- Inner Stack Trace -----
   at OcrLineTool.Tests.OcrClientHttpTests.<>c__DisplayClass3_0.<<TencentRecognizeAsyncObservesCancellationAndMapsConnectionFailure>b__0>d.MoveNext() in D:\a\NS\NS\OcrLineTool.Tests\OcrClientHttpTests.cs:line 98
--- End of stack trace from previous location ---
   at OcrLineTool.Tests.OcrClientHttpTests.FakeHttpMessageHandler.SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) in D:\a\NS\NS\OcrLineTool.Tests\OcrClientHttpTests.cs:line 401
   at System.Net.Http.HttpClient.<SendAsync>g__Core|83_0(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationTokenSource cts, Boolean disposeCts, CancellationTokenSource pendingRequestsCts, CancellationToken originalCancellationToken)
[xUnit.net 00:00:01.84]     OcrLineTool.Tests.HongrenguanTests.AutomaticAndManualReplayUseTheSameRoutesWithoutDuplicates [FAIL]
  Failed OcrLineTool.Tests.HongrenguanTests.AutomaticAndManualReplayUseTheSameRoutesWithoutDuplicates [37 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 2
Actual:   8
  Stack Trace:
     at OcrLineTool.Tests.HongrenguanTests.AutomaticAndManualReplayUseTheSameRoutesWithoutDuplicates() in D:\a\NS\NS\OcrLineTool.Tests\HongrenguanTests.cs:line 88
--- End of stack trace from previous location ---
[xUnit.net 00:00:02.05]     OcrLineTool.Tests.MainFormTests.AutomaticFolderRefreshPreservesSelectionResultsAndBusyStateWithoutScanningImages [FAIL]
[xUnit.net 00:00:02.06]     OcrLineTool.Tests.RuleEngineTests.UsesFolderNameThenLocalTypeToSelectTheRightImage [FAIL]
  Failed OcrLineTool.Tests.MainFormTests.AutomaticFolderRefreshPreservesSelectionResultsAndBusyStateWithoutScanningImages [31 ms]
  Error Message:
   System.ObjectDisposedException : The CancellationTokenSource has been disposed.
  Stack Trace:
     at System.Threading.CancellationTokenSource.Cancel()
   at OcrLineTool.MainForm.Dispose(Boolean disposing)
   at System.ComponentModel.Component.Dispose()
   at OcrLineTool.Tests.MainFormTests.AutomaticFolderRefreshPreservesSelectionResultsAndBusyStateWithoutScanningImages() in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 681
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed OcrLineTool.Tests.RuleEngineTests.UsesFolderNameThenLocalTypeToSelectTheRightImage [16 ms]
  Error Message:
   Assert.Equal() Failure: Collections differ
                                                ↓ (pos 0)
Expected: <generated>                          ["小灰灰两肖"]
Actual:   SelectArrayIterator<OcrRule, string> ["小灰灰一肖", "小灰灰两肖", "小灰灰两尾"]
                                                ↑ (pos 0)
  Stack Trace:
     at OcrLineTool.Tests.RuleEngineTests.UsesFolderNameThenLocalTypeToSelectTheRightImage() in D:\a\NS\NS\OcrLineTool.Tests\RuleEngineTests.cs:line 140
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
[xUnit.net 00:00:02.07]     OcrLineTool.Tests.RuleEngineTests.UsesFormulaFolderAsIdentityWhenFormulaTitleIsOmitted [FAIL]
  Failed OcrLineTool.Tests.RuleEngineTests.UsesFormulaFolderAsIdentityWhenFormulaTitleIsOmitted [< 1 ms]
  Error Message:
   Assert.Equal() Failure: Collections differ
                                                          ↓ (pos 1)
Expected: <generated>                          ["公式杀两肖肖", "公式杀两尾尾"]
Actual:   SelectArrayIterator<OcrRule, string> ["公式杀两肖肖"]
  Stack Trace:
     at OcrLineTool.Tests.RuleEngineTests.UsesFormulaFolderAsIdentityWhenFormulaTitleIsOmitted() in D:\a\NS\NS\OcrLineTool.Tests\RuleEngineTests.cs:line 401
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
[xUnit.net 00:00:02.59]     OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [FAIL]
  Failed OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [19 ms]
  Error Message:
   folderList bounds {X=27,Y=379,Width=320,Height=1} should fit inside  client area {X=0,Y=0,Width=374,Height=291}.
  Stack Trace:
     at OcrLineTool.Tests.MainFormTests.AssertControlFitsItsParent(Control control) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 870
   at OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(Single scale) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 538
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span`1 copyOfArgs, BindingFlags invokeAttr)
[xUnit.net 00:00:03.37]     OcrLineTool.Tests.TemplateSelectionTests.PartialFirstRunKeepsMatchedCandidateAndLeavesOtherRuleMissing [FAIL]
[xUnit.net 00:00:03.39]     OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "missing-file") [FAIL]
[xUnit.net 00:00:03.41]     OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "wrong-rule-ids") [FAIL]
[xUnit.net 00:00:03.43]     OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "invalid-json") [FAIL]
[xUnit.net 00:00:03.44]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: False) [FAIL]
[xUnit.net 00:00:03.46]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: True) [FAIL]
[xUnit.net 00:00:03.48]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: False) [FAIL]
[xUnit.net 00:00:03.50]     OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: True) [FAIL]
  Failed OcrLineTool.Tests.TemplateSelectionTests.PartialFirstRunKeepsMatchedCandidateAndLeavesOtherRuleMissing [23 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.PartialFirstRunKeepsMatchedCandidateAndLeavesOtherRuleMissing() in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 36
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "missing-file") [17 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(String failure) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 60
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "wrong-rule-ids") [19 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(String failure) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 60
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "invalid-json") [15 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(String failure) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 60
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: False) [16 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: True) [15 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: False) [17 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
  Failed OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: True) [18 ms]
  Error Message:
   OcrLineTool.OcrException : CUDA 标题指纹计算失败（代码 35）。
  Stack Trace:
     at OcrLineTool.VisualTemplateMatcher.CreateCudaFingerprint(Bitmap source, Double top, Double bottom, Double shift)
   at OcrLineTool.VisualTemplateMatcher.CreateFingerprint(String imagePath, Double verticalShiftWidthRatio)
   at OcrLineTool.Tests.TemplateSelectionTests.SelectionFixture..ctor(Boolean premium, Boolean strict) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 106
   at OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(Boolean premium, Boolean retry) in D:\a\NS\NS\OcrLineTool.Tests\TemplateSelectionTests.cs:line 23
--- End of stack trace from previous location ---
Results File: D:\a\NS\NS\audit-test-results\release.trx

Failed!  - Failed:    24, Passed:   799, Skipped:     0, Total:   823, Duration: 3 s - OcrLineTool.Tests.dll (net8.0)
~~~~

