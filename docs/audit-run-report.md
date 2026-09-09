# Audit validation run 34333215288

Commit tested: 09c8faf788fe3fa8c3e5911dd145238a90b3a545 plus the explicit applied source manifest.

Apply: success; Python: success; Debug: failure; Release: failure.

CUDA and ModelAssets cases are excluded on this hosted VM. No production OCR, secrets, deployment or image folders are used.

## debug.trx counters

<Counters total="815" executed="815" passed="814" failed="1" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" />

### OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698)

folderList bounds {X=27,Y=379,Width=320,Height=1} should fit inside  client area {X=0,Y=0,Width=374,Height=291}.

## release.trx counters

<Counters total="815" executed="815" passed="814" failed="1" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" />

### OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698)

folderList bounds {X=27,Y=379,Width=320,Height=1} should fit inside  client area {X=0,Y=0,Width=374,Height=291}.

## apply.log

~~~~text
Reviewed changed paths: 
~~~~

## debug.log

~~~~text
  Determining projects to restore...
  Restored D:\a\NS\NS\OcrLineTool.Tests\OcrLineTool.Tests.csproj (in 11.71 sec).
  Restored D:\a\NS\NS\OcrLineTool.App\OcrLineTool.App.csproj (in 12.87 sec).
  OcrLineTool.App -> D:\a\NS\NS\OcrLineTool.App\bin\Debug\net8.0-windows\win-x64\OCR整行提取工具-NVIDIA-CUDA.dll
  OcrLineTool.Tests -> D:\a\NS\NS\OcrLineTool.Tests\bin\Debug\net8.0-windows\OcrLineTool.Tests.dll
Test run for D:\a\NS\NS\OcrLineTool.Tests\bin\Debug\net8.0-windows\OcrLineTool.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:02.09]     OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [FAIL]
  Failed OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [70 ms]
  Error Message:
   folderList bounds {X=27,Y=379,Width=320,Height=1} should fit inside  client area {X=0,Y=0,Width=374,Height=291}.
  Stack Trace:
     at OcrLineTool.Tests.MainFormTests.AssertControlFitsItsParent(Control control) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 871
   at OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(Single scale) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 539
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span`1 copyOfArgs, BindingFlags invokeAttr)
Results File: D:\a\NS\NS\audit-test-results\debug.trx

Failed!  - Failed:     1, Passed:   814, Skipped:     0, Total:   815, Duration: 2 s - OcrLineTool.Tests.dll (net8.0)
~~~~

## excluded.log

~~~~text
Test run for D:\a\NS\NS\OcrLineTool.Tests\bin\Debug\net8.0-windows\OcrLineTool.Tests.dll (.NETCoreApp,Version=v8.0)
The following Tests are available:
    OcrLineTool.Tests.PublishConfigurationTests.CopiesCudaModelsFromTheSiblingModelDirectory
    OcrLineTool.Tests.SixCardRegressionTests.LeifengTemplateMatchesTheRealTitle
    OcrLineTool.Tests.SixCardRegressionTests.ExtractsConfirmedValueUsingActualConfiguration(group: "新澳高级会员", id: "翩翩公子尾", lines: ["扁羽扁羽公子九尾", "248期公子送尾数:0123467开??", "89准!", "247期公子送尾数:0123457兔40中", "89准!", ···], expected: "5尾")
    OcrLineTool.Tests.SixCardRegressionTests.ExtractsConfirmedValueUsingActualConfiguration(group: "蜻蜓一套骁腾", id: "骁腾杀肖", lines: ["肖", "234期:杀一肖《鸡》开:龙39准", "235期:杀一肖《马》开:猪32准", "236期:杀一肖《牛》开:猴11准", "237期:杀一肖《虎》开:羊12准", ···], expected: "鸡")
    OcrLineTool.Tests.SixCardRegressionTests.ExtractsConfirmedValueUsingActualConfiguration(group: "新澳六合彩资料", id: "内幕", lines: ["内幕网[必中36码]", "01020607080911131415161718", "248期19 21 22 24 25 26 27 29 30 31 32 33 35开??", "36374041424344464849", "01 03 04 05 06 07 08 09 10 11 12 13 14", ···], expected: "01 02 06 07 08 09 11 13 14 15 16 17 18 19 21 22 24"···)
    OcrLineTool.Tests.SixCardRegressionTests.ExtractsConfirmedValueUsingActualConfiguration(group: "新澳六合彩资料", id: "天线宝杀", lines: ["天线宝宝--杀12码", "12码12码→03 06 19 21 27 28 29 31 32 35 38", "248期开??", "48←精选杀", "12码→05323942", ···], expected: "03 06 19 21 27 28 29 31 32 35 38 48")
    OcrLineTool.Tests.SixCardRegressionTests.ExtractsConfirmedValueUsingActualConfiguration(group: "新澳六合彩资料", id: "狗庄", lines: ["狗庄吃12码收单省心", "0412151827293039434546", "248期开??", "48", "14153639", ···], expected: "04 12 15 18 27 29 30 39 43 45 46 48")
    OcrLineTool.Tests.SixCardRegressionTests.ExtractsConfirmedValueUsingActualConfiguration(group: "新澳六合彩资料", id: "雷锋", lines: ["雷锋绝杀--四肖四码", "248期 猪(32) 兔(28) 羊(24) 猴(35) 开??", "247期 羊(24) 兔(28) 龍(03) 猴(47) 兔40错"], expected: "32 28 24 35")
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳高级会员", id: "翩翩公子尾", lines: ["248期公子送尾数:0123467开??", "88准!"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳高级会员", id: "翩翩公子尾", lines: ["248期公子送尾数:0123467开??", "247期89准!"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳高级会员", id: "翩翩公子尾", lines: ["248期公子送尾数:0123467开??", "8?准!"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳高级会员", id: "翩翩公子尾", lines: ["248期公子送尾数:0123467开??", "89准!", "248期公子送尾数:0123457开??", "89准!"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "蜻蜓一套骁腾", id: "骁腾杀肖", lines: ["247期杀一肖《牛》开兔40准", "248期杀一《鸡》开發00准", "248期杀一《狗》开發00准"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "蜻蜓一套骁腾", id: "骁腾杀肖", lines: ["248期杀一《鸡?》开發00准"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "蜻蜓一套骁腾", id: "骁腾杀肖", lines: ["248期杀一", "247期杀一肖《鸡》开發00准"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "蜻蜓一套骁腾", id: "骁腾杀肖", lines: ["248期杀一开鸡00准"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳六合彩资料", id: "雷锋", lines: ["248期猪(32)兔(28)羊(24)猴(32)开??"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳六合彩资料", id: "雷锋", lines: ["248期猪(32)兔(28)羊(24)开??", "247期猴(35)"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳六合彩资料", id: "狗庄", lines: ["0412151827293039434546", "248期开??", "46", "14153639", "247期兔40中"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳六合彩资料", id: "狗庄", lines: ["0412151827293039434546", "248期开??", "50", "247期兔40中"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳六合彩资料", id: "狗庄", lines: ["0412151827293039434546", "248期开??", "4", "247期兔40中"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳六合彩资料", id: "狗庄", lines: ["0412151827293039434546", "248期开??", "48 49", "247期兔40中"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳六合彩资料", id: "狗庄", lines: ["0412151827293039434546", "248期开??", "247期48"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳六合彩资料", id: "天线宝杀", lines: ["12码→03 06 19 21 27 28 29 31 32 35 38", "248期开??", "38←精选杀", "247期兔40中"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳六合彩资料", id: "天线宝杀", lines: ["12码→03 06 19 21 27 28 29 31 32 35", "248期开??", "48←精选杀", "247期兔40中"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳六合彩资料", id: "天线宝杀", lines: ["12码→03 06 19 21 27 28 29 31 32 35 38", "248期开??", "247期48←精选杀"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳六合彩资料", id: "内幕", lines: ["01020607080911131415161718", "248期19 21 22 24 25 26 27 29 30 31 32 33 35开??", "36374041424344464848", "247期"])
    OcrLineTool.Tests.SixCardRegressionTests.RejectsIncompleteConflictingOrCrossIssueRows(group: "新澳六合彩资料", id: "内幕", lines: ["01020607080911131415161718", "248期19 21 22 24 25 26 27 29 30 31 32 33 35开??", "247期36374041424344464849"])
    OcrLineTool.Tests.SixCardRegressionTests.ReadsANonFirstRowAndIgnoresBrokenOlderRows(group: "新澳六合彩资料", id: "狗庄", first: "01 02 04 20 21 26 27 28 31 35 43", last: "49", expected: "01 02 04 20 21 26 27 28 31 35 43 49")
    OcrLineTool.Tests.SixCardRegressionTests.ReadsANonFirstRowAndIgnoresBrokenOlderRows(group: "新澳六合彩资料", id: "天线宝杀", first: "12码→11 15 26 28 31 34 41 43 44 45 46", last: "48←精选杀", expected: "11 15 26 28 31 34 41 43 44 45 46 48")
    OcrLineTool.Tests.SixCardRegressionTests.NewWrappedRowHandlingDoesNotApplyToOtherRules
    OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: False)
    OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: False, retry: True)
    OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: False)
    OcrLineTool.Tests.TemplateSelectionTests.FixedGroupSelectionKeepsLaterIssueRowsInTheSharedCrop(premium: True, retry: True)
    OcrLineTool.Tests.TemplateSelectionTests.PartialFirstRunKeepsMatchedCandidateAndLeavesOtherRuleMissing
    OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "invalid-json")
    OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "missing-file")
    OcrLineTool.Tests.TemplateSelectionTests.BrokenFirstRunCatalogStopsInsteadOfLaunchingLocalOcr(failure: "wrong-rule-ids")
    OcrLineTool.Tests.VisualTemplateMatcherTests.EnablesVisualTemplatesForBothFixedLayoutGroups
    OcrLineTool.Tests.VisualTemplateMatcherTests.MatchesFixedTitleWhenTheCurrentRowChanges
    OcrLineTool.Tests.VisualTemplateMatcherTests.RejectsPartialTemplateMatchesSoMissingRulesFallBackToLocalScan
    OcrLineTool.Tests.VisualTemplateMatcherTests.ValidatesTemplateRuleSetAgainstCompleteCatalogRules
    OcrLineTool.Tests.VisualTemplateMatcherTests.SelectsOnlyRequestedRulesFromSharedTemplateForRetry
    OcrLineTool.Tests.VisualTemplateMatcherTests.RejectsRetrySubsetWhenAnyRuleHasNoTemplateOrOverlaps
    OcrLineTool.Tests.VisualTemplateMatcherTests.FollowsSmallVerticalTitleShiftWhenCropping
    OcrLineTool.Tests.VisualTemplateMatcherTests.RestrictsTitleSearchToTheConfiguredSmallOffsetRange
    OcrLineTool.Tests.VisualTemplateMatcherTests.MatchesTemplatesUsingCatalogAndPerTemplateFingerprintRegions
    OcrLineTool.Tests.VisualTemplateMatcherTests.LoadsPremiumCatalogAndRejectsInvalidFingerprintRegions
    OcrLineTool.Tests.VisualTemplateMatcherTests.ProductionCatalogHasSixtySixTemplatesCoveringSixtySevenRules
    OcrLineTool.Tests.VisualTemplateMatcherTests.PremiumProductionCatalogCoversAllTenRulesWithDedicatedFingerprintRegions
~~~~

## python.log

~~~~text
OCR_PROGRESS|1|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-xtosk_7y\杰少\sample.png
OCR_PROGRESS|2|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-xtosk_7y\其他\sample.png
OCR_PROGRESS|1|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-8qte855o\杰少\sample.png
OCR_PROGRESS|2|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-8qte855o\其他\sample.png
..............
----------------------------------------------------------------------
Ran 14 tests in 0.106s

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
[xUnit.net 00:00:02.32]     OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [FAIL]
  Failed OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [82 ms]
  Error Message:
   folderList bounds {X=27,Y=379,Width=320,Height=1} should fit inside  client area {X=0,Y=0,Width=374,Height=291}.
  Stack Trace:
     at OcrLineTool.Tests.MainFormTests.AssertControlFitsItsParent(Control control) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 871
   at OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(Single scale) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 539
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span`1 copyOfArgs, BindingFlags invokeAttr)
Results File: D:\a\NS\NS\audit-test-results\release.trx

Failed!  - Failed:     1, Passed:   814, Skipped:     0, Total:   815, Duration: 2 s - OcrLineTool.Tests.dll (net8.0)
~~~~

