@echo off
git -C "%~dp0.." submodule update --init --recursive --remote
if errorlevel 1 exit /b %errorlevel%

pause
