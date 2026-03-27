@echo off
chcp 65001 >nul
cd /d "%~dp0backend"
echo [Backend] Restoring...
dotnet restore BookingTemplate.sln
if errorlevel 1 (
  echo [Backend] dotnet restore 失败
  pause
  exit /b 1
)
echo [Backend] Running API...
dotnet run --project src/BookingTemplate.Api
pause
