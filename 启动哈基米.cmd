@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
if exist "哈基米.exe" (
    start "" "哈基米.exe"
    exit /b 0
)
if exist "release\Hajimi\Hajimi.exe" (
    start "" "release\Hajimi\Hajimi.exe"
    exit /b 0
)
dotnet build "src\Hajimi\Hajimi.csproj" -c Release --nologo
if errorlevel 1 (
    echo Build failed. Install the .NET 10 SDK or see README.md.
    pause
    exit /b 1
)
start "" "src\Hajimi\bin\Release\net10.0-windows\Hajimi.exe"
