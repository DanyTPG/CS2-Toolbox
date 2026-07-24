@echo off
set CSC_PATH=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC_PATH%" (
    echo Error: C# compiler not found at %CSC_PATH%
    pause
    exit /b 1
)

echo Compiling CS2Assistant.cs...
"%CSC_PATH%" /unsafe /target:exe /out:CS2Assistant.exe /r:System.Drawing.dll /r:System.Windows.Forms.dll CS2Assistant.cs

if %ERRORLEVEL% equ 0 (
    echo Compilation successful! Created CS2Assistant.exe
) else (
    echo Compilation failed.
)
