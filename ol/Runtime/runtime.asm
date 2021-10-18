section .text
    global _start
    extern main

_start:
    pop rdi
    mov rsi, rsp
    add rsp, 8
    call main

    mov rdi, rax
    mov rax, 60
    syscall
