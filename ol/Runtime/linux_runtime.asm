section .text
    global _start
    extern __start

_start:
    pop rdi
    mov rsi, rsp
    add rsp, 8
    call __start
