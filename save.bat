@echo off
cd /d "%~dp0"
echo Files changed since the last save:
git status --short
echo.
set /p MSG=Short description of this save (press Enter for "work in progress"): 
if "%MSG%"=="" set MSG=work in progress
git add -A
git commit -m "%MSG%"
git push -u origin HEAD
echo.
echo Done. If you see an error above, send it to me.
pause
