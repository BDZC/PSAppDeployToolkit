net session >nul 2>&1
if ERRORLEVEL 1 powershell.exe -NoProfile -Command Start-Process -FilePath '%~0' -Verb RunAs & exit

"%~dp0PsExec64.exe" /accepteula /s /i "%~dp0Invoke-AppDeployToolkit.exe" -DeploymentType Repair -DeployMode Interactive
