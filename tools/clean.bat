@echo off
setlocal enableextensions
pushd %~dp0..

if exist ..\src\bin rmdir /s /q ..\src\bin
if exist ..\src\obj rmdir /s /q ..\src\obj

echo Cleaned.
popd
