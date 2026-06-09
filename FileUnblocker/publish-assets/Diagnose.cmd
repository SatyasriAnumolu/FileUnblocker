@echo off
:: ???????????????????????????????????????????????????????????????????????????
:: FileUnblocker — Diagnostic Script
::
:: Run this on the FAILING machine to identify why the app crashes.
:: Copy output and share it for troubleshooting.
:: ???????????????????????????????????????????????????????????????????????????
setlocal EnableDelayedExpansion

echo.
echo   ??????????????????????????????????????????????
echo     FileUnblocker Diagnostic Report
echo   ??????????????????????????????????????????????
echo.

:: ?? 1. System info ??
echo [1] SYSTEM INFO
echo     OS:   %OS%
for /f "tokens=2 delims==" %%a in ('wmic os get Caption /value 2^>nul ^| find "="') do echo     Name: %%a
for /f "tokens=2 delims==" %%a in ('wmic os get OSArchitecture /value 2^>nul ^| find "="') do echo Arch: %%a
echo     CPU:  %PROCESSOR_ARCHITECTURE%
echo.

:: ?? 2. Check if exe exists ??
echo [2] EXE CHECK
set "EXEPATH=%~dp0FileUnblocker.exe"
if exist "%EXEPATH%" (
    echo     Found: %EXEPATH%
    for %%F in ("%EXEPATH%") do echo     Size:  %%~zF bytes
) else (
    echo     ERROR: FileUnblocker.exe not found in %~dp0
    echo     Place this script in the same folder as FileUnblocker.exe
 goto :end
)
echo.

:: ?? 3. Check if exe is blocked (Zone.Identifier) ??
echo [3] BLOCK STATUS
powershell -NoProfile -Command "if(Get-Item '%EXEPATH%' -Stream Zone.Identifier -ErrorAction SilentlyContinue){'    STATUS: *** BLOCKED *** - This is likely the cause!'; '  FIX: Run Start.cmd or right-click exe -> Properties -> Unblock'}else{'    STATUS: Not blocked (OK)'}"
echo.

:: ?? 4. Check extraction cache ??
echo [4] EXTRACTION CACHE
set "CACHE=%LOCALAPPDATA%\Temp\.net\FileUnblocker"
if exist "%CACHE%" (
    echo     Cache exists: %CACHE%
    for /f %%A in ('dir /s /b "%CACHE%\*.dll" 2^>nul ^| find /c /v ""') do echo  DLLs in cache: %%A
    echo     Recommendation: Delete this folder and retry
    echo     Command: rmdir /s /q "%CACHE%"
) else (
    echo     No cache found (first run or already cleaned)
)
echo.

:: ?? 5. Check VC++ Runtime ??
echo [5] VC++ RUNTIME
where vcruntime140.dll >nul 2>&1
if %ERRORLEVEL%==0 (
    echo     vcruntime140.dll: Found in PATH
) else (
    if exist "%SystemRoot%\System32\vcruntime140.dll" (
        echo     vcruntime140.dll: Found in System32
  ) else (
    echo     vcruntime140.dll: *** NOT FOUND ***
        echo     This may cause 0xc0000005 crashes
     echo     Download: https://aka.ms/vs/17/release/vc_redist.x64.exe
    )
)
echo.

:: ?? 6. Check .NET (should not be needed for self-contained) ??
echo [6] .NET RUNTIME (informational only - not required for self-contained)
where dotnet >nul 2>&1
if %ERRORLEVEL%==0 (
    dotnet --list-runtimes 2>nul | findstr /i "WindowsDesktop" >nul
    if !ERRORLEVEL!==0 (
echo     .NET Desktop Runtime: Installed
    ) else (
        echo     .NET Desktop Runtime: Not installed (OK - app is self-contained)
    )
) else (
    echo     dotnet CLI: Not installed (OK - app is self-contained)
)
echo.

:: ?? 7. Architecture check ??
echo [7] ARCHITECTURE COMPATIBILITY
if "%PROCESSOR_ARCHITECTURE%"=="AMD64" (
  echoProcessor: x64 (compatible)
) else if "%PROCESSOR_ARCHITECTURE%"=="x86" (
    echo     Processor: *** x86 - INCOMPATIBLE ***
    echo     The app requires 64-bit Windows
) else if "%PROCESSOR_ARCHITECTURE%"=="ARM64" (
    echo     Processor: *** ARM64 ***
    echo     May work via emulation but could crash
    echo     Rebuild with: dotnet publish -r win-arm64
) else (
    echo     Processor: %PROCESSOR_ARCHITECTURE% (unknown)
)
echo.

:: ?? 8. Antivirus check ??
echo [8] ANTIVIRUS
powershell -NoProfile -Command "Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction SilentlyContinue | ForEach-Object { '    AV: ' + $_.displayName }"
echo   TIP: Temporarily disable real-time protection and retry
echo.

:: ?? 9. Windows Event Log (last crash) ??
echo [9] RECENT CRASH EVENTS (last 5)
powershell -NoProfile -Command "Get-WinEvent -FilterHashtable @{LogName='Application';ID=1000;Level=2} -MaxEvents 5 -ErrorAction SilentlyContinue | Where-Object {$_.Message -like '*FileUnblocker*'} | ForEach-Object { '    ' + $_.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss') + ' - Exception: ' + ($_.Message -split [Environment]::NewLine)[6] }"
echo.

:: ?? 10. Recommended fixes ??
echo   ??????????????????????????????????????????????
echo     RECOMMENDED FIXES (try in order):
echo   ??????????????????????????????????????????????
echo.
echo     1. Run Start.cmd (unblocks exe then launches)
echo.
echo  2. Delete extraction cache:
echo  rmdir /s /q "%LOCALAPPDATA%\Temp\.net\FileUnblocker"
echo        Then run Start.cmd again
echo.
echo     3. Disable antivirus temporarily, then retry
echo.
echo   4. If ARM64 machine: request ARM64 build
echo.
echo     5. Install VC++ Redistributable:
echo        https://aka.ms/vs/17/release/vc_redist.x64.exe
echo.

:end
echo   ??????????????????????????????????????????????
echo     Press any key to exit
echo   ??????????????????????????????????????????????
pause >nul
