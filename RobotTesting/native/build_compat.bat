@echo off
:: Build soem_wrapper_compat.dll with MSVC (x64).
:: Run from a "x64 Native Tools Command Prompt for VS 20xx".
:: Requires soem/soem.h (SOEM headers) in the same directory.

setlocal

set SRC=soem_wrapper_compat.c
set OBJ=soem_wrapper_compat.obj
set DEF=soem_wrapper_compat.def
set LIB=soem.lib
set OUT=soem_wrapper_compat.dll
set IMP=soem_wrapper_compat.lib

echo [1/2] Compiling %SRC% ...
cl /nologo /c /O2 /W3 /WX /MT ^
   /DWIN32 /D_WINDOWS /DNDEBUG /D_CRT_SECURE_NO_WARNINGS ^
   /I. /I./soem ^
   /Fo%OBJ% %SRC%
if errorlevel 1 goto :fail

echo [2/2] Linking %OUT% ...
link /nologo /DLL /OUT:%OUT% /DEF:%DEF% /IMPLIB:%IMP% ^
     /MACHINE:X64 ^
     %OBJ% %LIB% ^
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
