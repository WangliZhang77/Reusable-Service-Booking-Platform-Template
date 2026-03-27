@echo off
chcp 65001 >nul
cd /d "%~dp0frontend"
if not exist node_modules (
  echo [Frontend] Installing dependencies...
  npm install
  if errorlevel 1 (
    echo [Frontend] npm install 失败
    pause
    exit /b 1
  )
)
echo [Frontend] Starting Vite...
npm run dev
pause
