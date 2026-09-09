# Audit validation run 34333475956

Commit tested: d4ada068c4a6a979fd51ff6e8ba23dc90aea9825 plus the explicit applied source manifest.

Apply: success; Python: success; Debug: failure; Release: failure.

CUDA and ModelAssets cases are excluded on this hosted VM. No production OCR, secrets, deployment or image folders are used.

## debug.trx counters

<Counters total="854" executed="854" passed="853" failed="1" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" />

### OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698)

folderList bounds {X=27,Y=379,Width=320,Height=1} should fit inside  client area {X=0,Y=0,Width=374,Height=291}.

## release.trx counters

<Counters total="854" executed="854" passed="853" failed="1" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" />

### OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698)

folderList bounds {X=27,Y=379,Width=320,Height=1} should fit inside  client area {X=0,Y=0,Width=374,Height=291}.

## apply.log

~~~~text
Reviewed changed paths: 
~~~~

## debug.log

~~~~text
  Determining projects to restore...
  Restored D:\a\NS\NS\OcrLineTool.Tests\OcrLineTool.Tests.csproj (in 10.75 sec).
  Restored D:\a\NS\NS\OcrLineTool.App\OcrLineTool.App.csproj (in 11.51 sec).
  OcrLineTool.App -> D:\a\NS\NS\OcrLineTool.App\bin\Debug\net8.0-windows\win-x64\OCR整行提取工具-NVIDIA-CUDA.dll
  OcrLineTool.Tests -> D:\a\NS\NS\OcrLineTool.Tests\bin\Debug\net8.0-windows\OcrLineTool.Tests.dll
Test run for D:\a\NS\NS\OcrLineTool.Tests\bin\Debug\net8.0-windows\OcrLineTool.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:02.30]     OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [FAIL]
  Failed OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [121 ms]
  Error Message:
   folderList bounds {X=27,Y=379,Width=320,Height=1} should fit inside  client area {X=0,Y=0,Width=374,Height=291}.
  Stack Trace:
     at OcrLineTool.Tests.MainFormTests.AssertControlFitsItsParent(Control control) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 871
   at OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(Single scale) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 539
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span`1 copyOfArgs, BindingFlags invokeAttr)
Results File: D:\a\NS\NS\audit-test-results\debug.trx

Failed!  - Failed:     1, Passed:   853, Skipped:     0, Total:   854, Duration: 6 s - OcrLineTool.Tests.dll (net8.0)
~~~~

## excluded.log

~~~~text
Test run for D:\a\NS\NS\OcrLineTool.Tests\bin\Debug\net8.0-windows\OcrLineTool.Tests.dll (.NETCoreApp,Version=v8.0)
The following Tests are available:
    OcrLineTool.Tests.PublishConfigurationTests.CopiesCudaModelsFromTheSiblingModelDirectory
    OcrLineTool.Tests.SixCardRegressionTests.LeifengTemplateMatchesTheRealTitle
    OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: False)
    OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: True)
    OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: False)
    OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: True)
    OcrLineTool.Tests.TemplateSelectionTests.PartialFirstRunKeepsMatchedCandidateAndLeavesOtherRuleMissing
    OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "invalid-json")
    OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "missing-file")
    OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "wrong-rule-ids")
    OcrLineTool.Tests.VisualTemplateMatcherTests.MatchesFixedTitleWhenTheCurrentRowChanges
    OcrLineTool.Tests.VisualTemplateMatcherTests.FollowsSmallVerticalTitleShiftWhenCropping
    OcrLineTool.Tests.VisualTemplateMatcherTests.RestrictsTitleSearchToTheConfiguredSmallOffsetRange
    OcrLineTool.Tests.VisualTemplateMatcherTests.MatchesTemplatesUsingCatalogAndPerTemplateFingerprintRegions
    OcrLineTool.Tests.VisualTemplateMatcherTests.ProductionCatalogHasSixtySixTemplatesCoveringSixtySevenRules
~~~~

## python.log

~~~~text
OCR_PROGRESS|1|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-a0cjhzm4\杰少\sample.png
OCR_PROGRESS|2|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-a0cjhzm4\其他\sample.png
OCR_PROGRESS|1|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-5579ee9t\杰少\sample.png
OCR_PROGRESS|2|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-5579ee9t\其他\sample.png
..............
----------------------------------------------------------------------
Ran 14 tests in 0.140s

OK
~~~~

## release.log

~~~~text
  Determining projects to restore...
  All projects are up-to-date for restore.
  OcrLineTool.App -> D:\a\NS\NS\OcrLineTool.App\bin\Release\net8.0-windows\win-x64\OCR整行提取工具-NVIDIA-CUDA.dll
  OcrLineTool.Tests -> D:\a\NS\NS\OcrLineTool.Tests\bin\Release\net8.0-windows\OcrLineTool.Tests.dll
Test run for D:\a\NS\NS\OcrLineTool.Tests\bin\Release\net8.0-windows\OcrLineTool.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.63]     OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [FAIL]
  Failed OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [106 ms]
  Error Message:
   folderList bounds {X=27,Y=379,Width=320,Height=1} should fit inside  client area {X=0,Y=0,Width=374,Height=291}.
  Stack Trace:
     at OcrLineTool.Tests.MainFormTests.AssertControlFitsItsParent(Control control) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 871
   at OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(Single scale) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 539
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span`1 copyOfArgs, BindingFlags invokeAttr)
Results File: D:\a\NS\NS\audit-test-results\release.trx

Failed!  - Failed:     1, Passed:   853, Skipped:     0, Total:   854, Duration: 3 s - OcrLineTool.Tests.dll (net8.0)
~~~~

