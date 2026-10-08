@echo off
setlocal
set "DATA_DIR=%LOCALAPPDATA%\CityPulse"
if not exist "%DATA_DIR%" mkdir "%DATA_DIR%"
set "ASPNETCORE_ENVIRONMENT=Development"
set "ASPNETCORE_URLS=http://127.0.0.1:4173"
set "ConnectionStrings__CityPulse=Data Source=%DATA_DIR%\citypulse.db"
start "CityPulse NOC" /min "%~dp0CityPulse.Api.exe"
powershell -NoProfile -Command "$end=(Get-Date).AddSeconds(30); while((Get-Date) -lt $end){try {$r=Invoke-WebRequest 'http://127.0.0.1:4173/api/health' -TimeoutSec 1; if($r.StatusCode -eq 200){exit 0}}catch{}; Start-Sleep -Milliseconds 500}; exit 1"
if errorlevel 1 (
  echo CityPulse nao iniciou no endereco local 127.0.0.1:4173.
  exit /b 1
)
start "" "http://127.0.0.1:4173"
