@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
set "TEMP=%~dp0temp"
set "TMP=%TEMP%"
if not exist "%TEMP%" mkdir "%TEMP%"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:naiwa.ico /win32manifest:Pet.manifest /out:..\奶蛙桌宠.exe /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Web.Extensions.dll Naiwa.cs Gaze.cs GazeTests.cs HeadPat.cs HeadPatTests.cs Recycling.cs Feeding.cs FeatureTests.cs AssemblyInfo.cs
if errorlevel 1 (pause & exit /b 1)
echo Built successfully. Keep the executable beside 素材.nwp and 原版笑声.wav.
pause
