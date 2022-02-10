#!/bin/sh

nasm -f elf64 ol/Runtime/linux_runtime.asm -o ol/Runtime/runtime.o
