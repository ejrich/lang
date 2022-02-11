@echo off

nasm -f win64 ol/Runtime/windows_runtime.asm -o ol/Runtime/runtime.obj
