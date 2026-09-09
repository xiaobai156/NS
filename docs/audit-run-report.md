# Audit validation run 34334197829

Commit tested: 0fd12bcecde28df91099ee6ece35e973f092abca plus the explicit applied source manifest.

Apply: success; Python: success; Debug: success; Release: success.

CUDA and ModelAssets cases are excluded on this hosted VM. No production OCR, secrets, deployment or image folders are used.

## debug.trx counters

<Counters total="854" executed="854" passed="854" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" />

## release.trx counters

<Counters total="854" executed="854" passed="854" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" />

## apply.log

~~~~text
Reviewed changed paths: 
Hosted test desktop: 1024x768 -> 1920x1080
~~~~

## debug.log

~~~~text
  Determining projects to restore...
  Restored D:\a\NS\NS\OcrLineTool.Tests\OcrLineTool.Tests.csproj (in 9.22 sec).
  Restored D:\a\NS\NS\OcrLineTool.App\OcrLineTool.App.csproj (in 10.11 sec).
  OcrLineTool.App -> D:\a\NS\NS\OcrLineTool.App\bin\Debug\net8.0-windows\win-x64\OCR整行提取工具-NVIDIA-CUDA.dll
  OcrLineTool.Tests -> D:\a\NS\NS\OcrLineTool.Tests\bin\Debug\net8.0-windows\OcrLineTool.Tests.dll
Test run for D:\a\NS\NS\OcrLineTool.Tests\bin\Debug\net8.0-windows\OcrLineTool.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
Results File: D:\a\NS\NS\audit-test-results\debug.trx

Passed!  - Failed:     0, Passed:   854, Skipped:     0, Total:   854, Duration: 2 s - OcrLineTool.Tests.dll (net8.0)
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
OCR_PROGRESS|1|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-4gyc2_fp\杰少\sample.png
OCR_PROGRESS|2|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-4gyc2_fp\其他\sample.png
OCR_PROGRESS|1|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-ruh9uiy5\杰少\sample.png
OCR_PROGRESS|2|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-ruh9uiy5\其他\sample.png
..............
----------------------------------------------------------------------
Ran 14 tests in 0.138s

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
Results File: D:\a\NS\NS\audit-test-results\release.trx

Passed!  - Failed:     0, Passed:   854, Skipped:     0, Total:   854, Duration: 2 s - OcrLineTool.Tests.dll (net8.0)
~~~~

