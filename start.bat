@echo off
setlocal
chcp 65001 >nul

set "ROOT=%~dp0"
set "BACKEND_DIR=%ROOT%backend"
set "FRONTEND_DIR=%ROOT%frontend"

echo ===========================================
echo   ChatboxWeb 一键启动
echo ===========================================
echo.

if not exist "%BACKEND_DIR%" (
  echo [错误] 未找到 backend 目录：%BACKEND_DIR%
  pause
  exit /b 1
)

if not exist "%FRONTEND_DIR%" (
  echo [错误] 未找到 frontend 目录：%FRONTEND_DIR%
  pause
  exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [错误] 未检测到 dotnet，请先安装 .NET SDK 8+
  pause
  exit /b 1
)

where node >nul 2>nul
if errorlevel 1 (
  echo [错误] 未检测到 node，请先安装 Node.js 20+
  pause
  exit /b 1
)

where npm >nul 2>nul
if errorlevel 1 (
  echo [错误] 未检测到 npm，请先安装 Node.js（包含 npm）
  pause
  exit /b 1
)

echo [1/2] 启动后端窗口...
start "ChatboxWeb Backend" "%ROOT%run-backend.bat"

echo [2/2] 启动前端窗口...
start "ChatboxWeb Frontend" "%ROOT%run-frontend.bat"

echo.
echo 已启动：
echo - Backend: 请查看后端窗口输出端口（通常 http://localhost:5000）
echo - Frontend: http://localhost:5173
echo.
echo 按任意键关闭此窗口...
pause >nul
endlocal
