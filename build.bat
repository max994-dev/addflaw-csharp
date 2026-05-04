@echo off
setlocal

set "ROOT=%~dp0"
set "PROJECT=%ROOT%AddFlaw\AddFlaw.csproj"
set "CONFIG=Release"

echo [1/2] Building %PROJECT% ...
dotnet build "%PROJECT%" -c %CONFIG%
if errorlevel 1 (
  echo Build failed.
  exit /b 1
)

echo [2/2] Publishing self-contained app (win-x64) ...
dotnet publish "%PROJECT%" -c %CONFIG% -r win-x64 --self-contained true -o "%ROOT%publish"
if errorlevel 1 (
  echo Publish failed.
  exit /b 1
)

echo.
echo Done.
echo Output: "%ROOT%publish\AddFlaw.exe"
exit /b 0
