# Audit validation run 34332579960

Commit tested: df16bc560fd6c973080a5a7a3c9bead1f02fb418

Apply: success; Python: success; Debug: failure; Release: failure.

CUDA-tagged hardware tests are excluded on this hosted VM. No production OCR, secrets, deployment or image folders are used.

## apply.log

~~~~text
Reviewed changed paths: 
~~~~

## debug.log

~~~~text
  Determining projects to restore...
  Restored D:\a\NS\NS\OcrLineTool.Tests\OcrLineTool.Tests.csproj (in 11.27 sec).
  Restored D:\a\NS\NS\OcrLineTool.App\OcrLineTool.App.csproj (in 12.44 sec).
  OcrLineTool.App -> D:\a\NS\NS\OcrLineTool.App\bin\Debug\net8.0-windows\win-x64\OCR整行提取工具-NVIDIA-CUDA.dll
  OcrLineTool.Tests -> D:\a\NS\NS\OcrLineTool.Tests\bin\Debug\net8.0-windows\OcrLineTool.Tests.dll
Test run for D:\a\NS\NS\OcrLineTool.Tests\bin\Debug\net8.0-windows\OcrLineTool.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.59]     OcrLineTool.Tests.PublishConfigurationTests.CopiesCudaModelsFromTheSiblingModelDirectory [FAIL]
  Failed OcrLineTool.Tests.PublishConfigurationTests.CopiesCudaModelsFromTheSiblingModelDirectory [1 ms]
  Error Message:
   Assert.True() Failure
Expected: True
Actual:   False
  Stack Trace:
     at OcrLineTool.Tests.PublishConfigurationTests.CopiesCudaModelsFromTheSiblingModelDirectory() in D:\a\NS\NS\OcrLineTool.Tests\PublishConfigurationTests.cs:line 36
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
[xUnit.net 00:00:01.92]     OcrLineTool.Tests.JieshaoTests.ExtractsAllFourJieshaoOutputsFromOneCard [FAIL]
[xUnit.net 00:00:01.93]     OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 0, expected: "鼠") [FAIL]
[xUnit.net 00:00:01.93]     OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 3, expected: "龙猴羊蛇虎兔鸡马狗") [FAIL]
[xUnit.net 00:00:01.94]     OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 2, expected: "5尾") [FAIL]
[xUnit.net 00:00:01.94]     OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 1, expected: "0尾") [FAIL]
  Failed OcrLineTool.Tests.JieshaoTests.ExtractsAllFourJieshaoOutputsFromOneCard [10 ms]
  Error Message:
   Assert.Equal() Failure: Strings differ
Expected: "鼠"
Actual:   null
  Stack Trace:
     at OcrLineTool.Tests.JieshaoTests.ExtractsAllFourJieshaoOutputsFromOneCard() in D:\a\NS\NS\OcrLineTool.Tests\JieshaoTests.cs:line 239
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 0, expected: "鼠") [5 ms]
  Error Message:
   Assert.Single() Failure: The collection contained 4 items
Collection: [OcrRule { Keyword = 杰少, Type = 生肖, Label = 杰少杀一肖, Section = 杀①肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一肖, OutputLabel = 杰少杀一肖 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少杀一尾, Section = 加杀尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一尾, OutputLabel = 杰少杀一尾 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少禁一尾, Section = 禁一尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少禁一尾, OutputLabel = 杰少禁一尾 }, OcrRule { Keyword = 杰少, Type = 九肖, Label = 杰少九肖, Section = 九肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少九肖, OutputLabel = 杰少九肖 }]
  Stack Trace:
     at OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(Int32 index, String expected) in D:\a\NS\NS\OcrLineTool.Tests\JieshaoTests.cs:line 176
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span`1 copyOfArgs, BindingFlags invokeAttr)
  Failed OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 3, expected: "龙猴羊蛇虎兔鸡马狗") [6 ms]
  Error Message:
   Assert.Single() Failure: The collection contained 4 items
Collection: [OcrRule { Keyword = 杰少, Type = 生肖, Label = 杰少杀一肖, Section = 杀①肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一肖, OutputLabel = 杰少杀一肖 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少杀一尾, Section = 加杀尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一尾, OutputLabel = 杰少杀一尾 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少禁一尾, Section = 禁一尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少禁一尾, OutputLabel = 杰少禁一尾 }, OcrRule { Keyword = 杰少, Type = 九肖, Label = 杰少九肖, Section = 九肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少九肖, OutputLabel = 杰少九肖 }]
  Stack Trace:
     at OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(Int32 index, String expected) in D:\a\NS\NS\OcrLineTool.Tests\JieshaoTests.cs:line 176
   at InvokeStub_JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
  Failed OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 2, expected: "5尾") [2 ms]
  Error Message:
   Assert.Single() Failure: The collection contained 4 items
Collection: [OcrRule { Keyword = 杰少, Type = 生肖, Label = 杰少杀一肖, Section = 杀①肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一肖, OutputLabel = 杰少杀一肖 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少杀一尾, Section = 加杀尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一尾, OutputLabel = 杰少杀一尾 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少禁一尾, Section = 禁一尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少禁一尾, OutputLabel = 杰少禁一尾 }, OcrRule { Keyword = 杰少, Type = 九肖, Label = 杰少九肖, Section = 九肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少九肖, OutputLabel = 杰少九肖 }]
  Stack Trace:
     at OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(Int32 index, String expected) in D:\a\NS\NS\OcrLineTool.Tests\JieshaoTests.cs:line 176
   at InvokeStub_JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
  Failed OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 1, expected: "0尾") [4 ms]
  Error Message:
   Assert.Single() Failure: The collection contained 4 items
Collection: [OcrRule { Keyword = 杰少, Type = 生肖, Label = 杰少杀一肖, Section = 杀①肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一肖, OutputLabel = 杰少杀一肖 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少杀一尾, Section = 加杀尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一尾, OutputLabel = 杰少杀一尾 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少禁一尾, Section = 禁一尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少禁一尾, OutputLabel = 杰少禁一尾 }, OcrRule { Keyword = 杰少, Type = 九肖, Label = 杰少九肖, Section = 九肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少九肖, OutputLabel = 杰少九肖 }]
  Stack Trace:
     at OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(Int32 index, String expected) in D:\a\NS\NS\OcrLineTool.Tests\JieshaoTests.cs:line 176
   at InvokeStub_JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
[xUnit.net 00:00:02.01]     OcrLineTool.Tests.JieshaoTests.FailedTailCacheMustNotPreventAnExplicitCloudRetry [FAIL]
  Failed OcrLineTool.Tests.JieshaoTests.FailedTailCacheMustNotPreventAnExplicitCloudRetry [4 ms]
  Error Message:
   Assert.True() Failure
Expected: True
Actual:   False
  Stack Trace:
     at OcrLineTool.Tests.JieshaoTests.FailedTailCacheMustNotPreventAnExplicitCloudRetry() in D:\a\NS\NS\OcrLineTool.Tests\JieshaoTests.cs:line 77
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
[xUnit.net 00:00:02.87]     OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [FAIL]
  Failed OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [28 ms]
  Error Message:
   folderList bounds {X=27,Y=379,Width=320,Height=1} should fit inside  client area {X=0,Y=0,Width=374,Height=291}.
  Stack Trace:
     at OcrLineTool.Tests.MainFormTests.AssertControlFitsItsParent(Control control) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 870
   at OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(Single scale) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 538
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span`1 copyOfArgs, BindingFlags invokeAttr)
Results File: D:\a\NS\NS\audit-test-results\debug.trx

Failed!  - Failed:     8, Passed:   807, Skipped:     0, Total:   815, Duration: 2 s - OcrLineTool.Tests.dll (net8.0)
~~~~

## python.log

~~~~text
OCR_PROGRESS|1|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-10en5fgt\杰少\sample.png
OCR_PROGRESS|2|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-10en5fgt\其他\sample.png
OCR_PROGRESS|1|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-_v01mic3\杰少\sample.png
OCR_PROGRESS|2|2|C:\Users\RUNNER~1\AppData\Local\Temp\ocr-compact-test-_v01mic3\其他\sample.png
..............
----------------------------------------------------------------------
Ran 14 tests in 0.146s

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
[xUnit.net 00:00:00.96]     OcrLineTool.Tests.JieshaoTests.ExtractsAllFourJieshaoOutputsFromOneCard [FAIL]
  Failed OcrLineTool.Tests.JieshaoTests.ExtractsAllFourJieshaoOutputsFromOneCard [9 ms]
  Error Message:
   Assert.Equal() Failure: Strings differ
Expected: "鼠"
Actual:   null
  Stack Trace:
     at OcrLineTool.Tests.JieshaoTests.ExtractsAllFourJieshaoOutputsFromOneCard() in D:\a\NS\NS\OcrLineTool.Tests\JieshaoTests.cs:line 239
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
[xUnit.net 00:00:00.99]     OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 0, expected: "鼠") [FAIL]
[xUnit.net 00:00:00.99]     OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 3, expected: "龙猴羊蛇虎兔鸡马狗") [FAIL]
[xUnit.net 00:00:00.99]     OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 2, expected: "5尾") [FAIL]
[xUnit.net 00:00:01.00]     OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 1, expected: "0尾") [FAIL]
  Failed OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 0, expected: "鼠") [13 ms]
  Error Message:
   Assert.Single() Failure: The collection contained 4 items
Collection: [OcrRule { Keyword = 杰少, Type = 生肖, Label = 杰少杀一肖, Section = 杀①肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一肖, OutputLabel = 杰少杀一肖 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少杀一尾, Section = 加杀尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一尾, OutputLabel = 杰少杀一尾 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少禁一尾, Section = 禁一尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少禁一尾, OutputLabel = 杰少禁一尾 }, OcrRule { Keyword = 杰少, Type = 九肖, Label = 杰少九肖, Section = 九肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少九肖, OutputLabel = 杰少九肖 }]
  Stack Trace:
     at OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(Int32 index, String expected) in D:\a\NS\NS\OcrLineTool.Tests\JieshaoTests.cs:line 176
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span`1 copyOfArgs, BindingFlags invokeAttr)
  Failed OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 3, expected: "龙猴羊蛇虎兔鸡马狗") [2 ms]
  Error Message:
   Assert.Single() Failure: The collection contained 4 items
Collection: [OcrRule { Keyword = 杰少, Type = 生肖, Label = 杰少杀一肖, Section = 杀①肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一肖, OutputLabel = 杰少杀一肖 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少杀一尾, Section = 加杀尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一尾, OutputLabel = 杰少杀一尾 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少禁一尾, Section = 禁一尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少禁一尾, OutputLabel = 杰少禁一尾 }, OcrRule { Keyword = 杰少, Type = 九肖, Label = 杰少九肖, Section = 九肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少九肖, OutputLabel = 杰少九肖 }]
  Stack Trace:
     at OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(Int32 index, String expected) in D:\a\NS\NS\OcrLineTool.Tests\JieshaoTests.cs:line 176
   at InvokeStub_JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
  Failed OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 2, expected: "5尾") [2 ms]
  Error Message:
   Assert.Single() Failure: The collection contained 4 items
Collection: [OcrRule { Keyword = 杰少, Type = 生肖, Label = 杰少杀一肖, Section = 杀①肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一肖, OutputLabel = 杰少杀一肖 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少杀一尾, Section = 加杀尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一尾, OutputLabel = 杰少杀一尾 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少禁一尾, Section = 禁一尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少禁一尾, OutputLabel = 杰少禁一尾 }, OcrRule { Keyword = 杰少, Type = 九肖, Label = 杰少九肖, Section = 九肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少九肖, OutputLabel = 杰少九肖 }]
  Stack Trace:
     at OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(Int32 index, String expected) in D:\a\NS\NS\OcrLineTool.Tests\JieshaoTests.cs:line 176
   at InvokeStub_JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
  Failed OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(index: 1, expected: "0尾") [2 ms]
  Error Message:
   Assert.Single() Failure: The collection contained 4 items
Collection: [OcrRule { Keyword = 杰少, Type = 生肖, Label = 杰少杀一肖, Section = 杀①肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一肖, OutputLabel = 杰少杀一肖 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少杀一尾, Section = 加杀尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少杀一尾, OutputLabel = 杰少杀一尾 }, OcrRule { Keyword = 杰少, Type = 尾, Label = 杰少禁一尾, Section = 禁一尾, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少禁一尾, OutputLabel = 杰少禁一尾 }, OcrRule { Keyword = 杰少, Type = 九肖, Label = 杰少九肖, Section = 九肖, RequiredKeyword = 原创杰少, Folder = 杰少, IgnoreIssue = False, AllowNearbyValue = False, AllowValueWithoutKeyword = False, StrictIssueBlock = False, SingleValuePerIssue = False, Id = 杰少九肖, OutputLabel = 杰少九肖 }]
  Stack Trace:
     at OcrLineTool.Tests.JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(Int32 index, String expected) in D:\a\NS\NS\OcrLineTool.Tests\JieshaoTests.cs:line 176
   at InvokeStub_JieshaoTests.SelectsSeparateMaterialsWithinJieshaoFolder(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
[xUnit.net 00:00:01.10]     OcrLineTool.Tests.JieshaoTests.FailedTailCacheMustNotPreventAnExplicitCloudRetry [FAIL]
  Failed OcrLineTool.Tests.JieshaoTests.FailedTailCacheMustNotPreventAnExplicitCloudRetry [5 ms]
  Error Message:
   Assert.True() Failure
Expected: True
Actual:   False
  Stack Trace:
     at OcrLineTool.Tests.JieshaoTests.FailedTailCacheMustNotPreventAnExplicitCloudRetry() in D:\a\NS\NS\OcrLineTool.Tests\JieshaoTests.cs:line 77
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
[xUnit.net 00:00:01.50]     OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [FAIL]
  Failed OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(scale: 1.35416698) [20 ms]
  Error Message:
   folderList bounds {X=27,Y=379,Width=320,Height=1} should fit inside  client area {X=0,Y=0,Width=374,Height=291}.
  Stack Trace:
     at OcrLineTool.Tests.MainFormTests.AssertControlFitsItsParent(Control control) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 870
   at OcrLineTool.Tests.MainFormTests.KeepsPrimaryAndRecoveryActionsInsideTheirCardsAtTheMinimumWindowSize(Single scale) in D:\a\NS\NS\OcrLineTool.Tests\MainFormTests.cs:line 538
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span`1 copyOfArgs, BindingFlags invokeAttr)
[xUnit.net 00:00:02.74]     OcrLineTool.Tests.PublishConfigurationTests.CopiesCudaModelsFromTheSiblingModelDirectory [FAIL]
  Failed OcrLineTool.Tests.PublishConfigurationTests.CopiesCudaModelsFromTheSiblingModelDirectory [< 1 ms]
  Error Message:
   Assert.True() Failure
Expected: True
Actual:   False
  Stack Trace:
     at OcrLineTool.Tests.PublishConfigurationTests.CopiesCudaModelsFromTheSiblingModelDirectory() in D:\a\NS\NS\OcrLineTool.Tests\PublishConfigurationTests.cs:line 36
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
Results File: D:\a\NS\NS\audit-test-results\release.trx

Failed!  - Failed:     8, Passed:   807, Skipped:     0, Total:   815, Duration: 3 s - OcrLineTool.Tests.dll (net8.0)
~~~~

