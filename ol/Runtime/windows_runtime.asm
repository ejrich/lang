bits 64
default rel

segment .text
    global __chkstk
    extern __start

__chkstk:
    ret
