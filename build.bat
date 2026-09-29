@echo off
rem Builds bin\Dungeons2SkinLoader.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
set "ROOT=%~dp0"
if not exist "%ROOT%data\md2data.bin" (
  echo data\md2data.bin is missing. See data\README.md.
  exit /b 1
)
if not exist "%ROOT%bin" mkdir "%ROOT%bin"
"%FW%\csc.exe" /nologo /optimize /target:winexe /out:"%ROOT%bin\Dungeons2SkinLoader.exe" /win32icon:"%ROOT%src\app.ico" ^
  /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Core.dll ^
  /r:"%FW%\System.IO.Compression.dll" /r:"%FW%\System.IO.Compression.FileSystem.dll" ^
  /resource:"%ROOT%data\md2data.bin",md2data.bin /resource:"%ROOT%src\font.ttf",font.ttf ^
  "%ROOT%src\App.cs" "%ROOT%src\Core.cs" "%ROOT%src\Cli.cs" "%ROOT%src\MainForm.cs" "%ROOT%src\ColorPicker.cs"
if errorlevel 1 exit /b 1
echo Built bin\Dungeons2SkinLoader.exe
