@echo off
if not exist bin mkdir bin
where cl >nul 2>nul
if %errorlevel%==0 (
  cl /nologo /LD /MT /O2 deliveryfee.c /Fe:bin\deliverynative.dll
) else (
  gcc -std=c99 -O2 -Wall -Wextra -shared -static -o bin\deliverynative.dll deliveryfee.c
)
dir bin
