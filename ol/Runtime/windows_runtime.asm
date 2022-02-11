    global _start
    global __chkstk
    global _fltused
    extern __start

section .text

_start:
    sub rsp, 28h
    xor rcx, rcx
    call __start
    add rsp, 28h
    ret

__chkstk:
    ret

_fltused:
    dw 0
