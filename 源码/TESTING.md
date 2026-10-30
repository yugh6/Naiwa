# 构建与测试

需要 Windows 10/11 和 .NET Framework 4.x。以下 PowerShell 命令均从 `源码` 目录运行。主程序和自测使用上一级目录的 `素材.nwp`、`原版笑声.wav`；无需下载依赖。

## 构建与主程序自测

先退出正在运行的奶蛙，再构建。`build.cmd` 的编译文件列表包含主程序、追视、投喂和测试代码，输出上一级目录的 `奶蛙桌宠.exe`；脚本结束时按任意键继续。

```powershell
.\build.cmd
$testOutput = [IO.Path]::GetFullPath((Join-Path (Get-Location) '..\test-output'))
New-Item -ItemType Directory -Force -Path $testOutput | Out-Null
$testProcess = Start-Process -FilePath '..\奶蛙桌宠.exe' -ArgumentList @('--self-test', ('"{0}"' -f $testOutput)) -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath (Join-Path $testOutput 'self-test.txt')
if ($testProcess.ExitCode -ne 0) { throw '奶蛙自测失败，请检查 self-test.txt。' }
```

成功日志以 `RESULT: PASS` 结束，并生成动作与追视方向 PNG。主程序自测覆盖原有动画、点击/拖动、全屏隐藏、资源释放，以及新增追视、交互、投喂状态和可恢复错误。投喂 UI 测试使用模拟回收器，不删除文件；校验测试只新建专用测试文件。

## Windows 实际回收测试

这项测试独立编译，不在桌宠正常启动或 `--self-test` 时自动运行。它仅操作指定输出目录中自动新建的唯一名称测试文件，验证取消、文件占用、部分成功和实际回收；不会接收现有文件作为待回收目标。

```powershell
$testOutput = [IO.Path]::GetFullPath((Join-Path (Get-Location) '..\test-output'))
New-Item -ItemType Directory -Force -Path $testOutput | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
& $compiler /nologo /target:exe /main:NaiwaDesktop.RecyclingIntegrationTests (('/out:{0}' -f (Join-Path $testOutput 'recycling-tests.exe'))) Recycling.cs RecyclingTests.cs
if ($LASTEXITCODE -ne 0) { throw '回收测试编译失败。' }
& (Join-Path $testOutput 'recycling-tests.exe') (Join-Path $testOutput 'recycle-fixtures')
if ($LASTEXITCODE -ne 0) { throw '回收测试失败，请检查 recycling-tests.txt。' }
```

日志保存在 `test-output\recycle-fixtures\recycling-tests.txt`。实际回收成功的测试文件会留在系统回收站；校验和失败测试的文件留在输出目录。

## 手动交互检查

- 在桌面上下左右移动鼠标，确认眼内追视；关闭「目光跟随鼠标」，确认恢复原版悬停转眼。
- 单击头部检查约 0.85 秒的轻压、低头眨眼与回弹；单击肚子检查局部回弹。双击肚子播放约 14 秒大笑，双击头部、脚部等位置不触发大笑；笑倒后单击站起。
- 检查拖动、跨屏移动、大小、声音开关和全屏隐藏仍然正常。
- 新建无用的测试文件，从文件资源管理器拖到奶蛙身上：检查悬停提示、移开取消、确认取消、回收成功反馈与回收站中的文件。仅用测试文件检查关闭确认后的操作。
- 拖入测试文件夹等不支持的项目，确认显示原因且不执行回收。使用缺失说明文件或无效测试 WAV 的独立副本，确认错误不会退出整个桌宠。

根目录的预览视频和旧预览图尚未包含新增交互；以本分支构建后的程序和自测输出为准。
