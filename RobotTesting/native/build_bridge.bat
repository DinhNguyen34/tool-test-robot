@echo off
:: Build soem_bridge.dll (x64 MSVC).
:: Run from an "x64 Native Tools Command Prompt for VS 20xx".

setlocal

set SRC=soem_bridge.c
set OBJ=soem_bridge.obj
set DEF=soem_bridge.def
set OUT=soem_bridge.dll
set IMP=soem_bridge.lib

echo [1/2] Compiling %SRC% ...
cl /nologo /c /O2 /W3 /WX /MT ^
   /DWIN32 /D_WINDOWS /DNDEBUG /D_CRT_SECURE_NO_WARNINGS ^
   /I. /I./soem ^
   /Fo%OBJ% %SRC%
if errorlevel 1 goto :fail

echo [2/2] Linking %OUT% ...
link /nologo /DLL /OUT:%OUT% /DEF:%DEF% /IMPLIB:%IMP% ^
     /MACHINE:X64 ^
     %OBJ% ^
     kernel32.lib
if errorlevel 1 goto :fail

echo.
echo Build succeeded: %OUT%
goto :end

:fail
echo.
echo Build FAILED.
exit /b 1

:end
endlocal
