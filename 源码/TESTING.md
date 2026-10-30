# 构建与测试

需要 Windows 10/11 和 .NET Framework 4.x。以下 PowerShell 命令均从 `源码` 目录运行。主程序和自测使用上一级目录的 `素材.nwp`、`原版笑声.wav`；无需下载依赖。

## 构建与主程序自测

先退出正在运行的奶蛙，再构建。`build.cmd` 包含主程序、互动、桌面适配、渲染、设置及测试代码，输出上一级目录的 `奶蛙桌宠.exe`；脚本结束时按任意键继续。

```powershell
.\build.cmd
$testOutput = [IO.Path]::GetFullPath((Join-Path (Get-Location) '..\test-output'))
New-Item -ItemType Directory -Force -Path $testOutput | Out-Null
$testProcess = Start-Process -FilePath '..\奶蛙桌宠.exe' -ArgumentList @('--self-test', ('"{0}"' -f $testOutput)) -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath (Join-Path $testOutput 'self-test.txt')
if ($testProcess.ExitCode -ne 0) { throw '奶蛙自测失败，请检查 self-test.txt。' }
```

成功日志以 `RESULT: PASS` 结束，并生成动作与追视方向 PNG。主程序自测覆盖：

- 原有动画、点击/拖动、全屏隐藏、资源释放，以及追视、头部/肚子分区、投喂状态和可恢复错误。
- 固定位置仍可点击、穿透时禁用点击与投喂、暂时隐藏与全屏规则的组合、托盘恢复，以及快捷键消息处理。
- 注入 96、144、192 DPI 的窗口消息，在三种大小下检查窗口尺寸、摸头与肚子双击命中。系统拖动阈值替代固定像素阈值。
- 待机检查频率、轻呼吸的开启/关闭与间隔、复用画布及形变缓冲后的像素还原、后台预解码的所有权转移与关闭时清理。
- 旧配置兼容、完整设置往返、损坏/截断配置从备份恢复，以及真实文件占用导致的保存失败保留原文件。

投喂 UI 测试使用模拟回收器，不删除文件；校验和配置测试只操作输出目录中新建的专用文件。配置测试不读写真实用户的 `%LOCALAPPDATA%\NaiwaDesktopPet`。模拟 DPI 消息和快捷键消息不等于已完成不同物理屏幕上的拖动或真实全局快捷键验证。

## 资源与桌面行为说明

- 交互或鼠标活动时定时器间隔为 16ms；稳定待机时为 80ms，轻呼吸且无其他活动时为 50ms，隐藏时为 150ms。画面内容不变时继续跳过重绘，这些间隔不等于承诺的实际帧率。
- 每个窗口复用输出画布、肚子形变位图及像素数组，尺寸变化时重建。大笑解码在后台提前准备最多 4 个后续帧，前台缓存最多 12 帧；未准备好的当前帧仍同步解码，保证内容和播放时序。
- 轻呼吸默认关闭；开启后等待约 2 秒开始，持续约 4 秒，每 20 秒重复。其他互动会暂停呼吸，并将下一次开始推迟至互动结束约 8 秒后；普通眨眼和追视不持续推迟呼吸。
- manifest 优先声明 PerMonitorV2，并为较旧 Windows 提供 DPI 感知回退。窗口处理 DPI 与工作区变化，点击坐标换算为原图坐标。物理显示器混合缩放的视觉效果仍需下面的人工检查。
- 配置先写同目录唯一临时文件并刷新，再原子替换 `settings.ini`，将上一份有效配置保留为 `settings.ini.bak`。从备份恢复后的首次保存不会用损坏主文件覆盖好备份。保存失败保留原文件、提示用户并记日志。

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
- 检查拖动、大小、声音开关和全屏隐藏仍然正常。开启「固定位置」后，拖动不移动也不触发松手点击；单击头部、单/双击肚子和文件投喂仍可使用。关闭后恢复拖动。
- 开启「鼠标穿透」，实际点击奶蛙实体下方的另一个应用，确认下面的控件收到点击、奶蛙不互动；拖入测试文件也不投喂。从托盘取消后恢复。透明背景原有的穿透行为应保持不变。
- 按真实的 Ctrl+Alt+N 和托盘菜单分别隐藏/显示，检查隐藏时停止大笑和笑声；手动隐藏后退出全屏、调整大小不能让它自动出现。全屏状态下请求显示仍保持自动隐藏。若快捷键已被占用，应提示并保留托盘入口。
- 分别通过「回到屏幕右下角」、双击托盘和重复启动找回奶蛙，确认解除暂时隐藏和鼠标穿透、保留固定位置设置。退出再启动后，不保留暂时隐藏状态。
- 用实际 100%、150%、200% 缩放的屏幕交叉拖动，检查视觉大小、清晰度、鼠标相对位置、头部/肚子命中与拖动稳定性。覆盖左侧负坐标屏幕、主屏切换、拔掉外接屏幕，以及任务栏/工作区变化；重复启动应回到鼠标所在屏幕。
- 开启轻呼吸并等待完整周期，确认轻微起伏与安静间隔、互动时暂停；关闭后恢复静止站姿。用任务管理器分别观察待机、追视、连续戳肚子与重复大笑的 CPU 和内存，避免把定时器设置值当成性能实测结果。
- 新建无用的测试文件，从文件资源管理器拖到奶蛙身上：检查悬停提示、移开取消、确认取消、回收成功反馈与回收站中的文件。仅用测试文件检查关闭确认后的操作。
- 拖入测试文件夹等不支持的项目，确认显示原因且不执行回收。使用缺失说明文件或无效测试 WAV 的独立副本，确认错误不会退出整个桌宠。

根目录的预览视频和旧预览图尚未包含新增交互；以本分支构建后的程序和自测输出为准。
