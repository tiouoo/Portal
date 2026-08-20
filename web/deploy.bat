@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

echo ============================================
echo   Portal web - One-click Deploy (Workers)
echo ============================================

REM ---- Load cf.env if present ----
if exist "cf.env" (
    for /f "usebackq tokens=1,* delims==" %%a in ("cf.env") do set "%%a=%%b"
)

REM ---- Detect local proxy on 7890 to reach api.cloudflare.com ----
powershell -NoProfile -Command "try { $c = New-Object System.Net.Sockets.TcpClient; $t = $c.ConnectAsync('127.0.0.1', 7890); if ($t.Wait(1500)) { exit 0 } else { exit 1 } } catch { exit 1 }" >nul 2>nul
if not errorlevel 1 (
    set "HTTPS_PROXY=http://127.0.0.1:7890"
    set "HTTP_PROXY=http://127.0.0.1:7890"
    echo [OK] proxy 127.0.0.1:7890 enabled
) else (
    echo [..] no proxy on 7890, direct connection
)

REM ---- Check API token ----
if "%CLOUDFLARE_API_TOKEN%"=="" (
    echo.
    echo [ERROR] CLOUDFLARE_API_TOKEN not found.
    echo Please create cf.env in this folder with:
    echo   CLOUDFLARE_API_TOKEN=your_token
    echo.
    pause
    exit /b 1
)

echo.
echo [1/3] Installing dependencies...
call npm ci
if errorlevel 1 (
    echo [WARN] npm ci failed, trying npm install ...
    call npm install
)

echo.
echo [2/3] Building (prerender / + /install)...
call npm run prerender
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed. Deploy aborted.
    pause
    exit /b 1
)

echo.
echo [3/3] Deploying to Cloudflare Workers...
call npx wrangler deploy
if errorlevel 1 (
    echo.
    echo [ERROR] Deploy failed.
    pause
    exit /b 1
)

echo.
echo ============================================
echo   Deploy complete!
echo   Live: https://portal.tiouo.cc
echo ============================================
pause
