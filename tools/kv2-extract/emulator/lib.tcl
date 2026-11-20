set throttle off
set renderer none
if {![info exists ::dumpdir]} { set ::dumpdir dump }
file mkdir $::dumpdir

proc dumpall {name} {
  foreach {dbg file} [list "physical VRAM" vram "VDP palette" pal "VDP regs" regs "memory" mem] {
    set f [open "$::dumpdir/${name}_$file.bin" wb]
    puts -nonewline $f [debug read_block $dbg 0 [debug size $dbg]]
    close $f
  }
}

# Teclas: linha/mascara da matriz do MSX.
# espaco 8/0x01 · esq 8/0x10 · cima 8/0x20 · baixo 8/0x40 · dir 8/0x80
# F1 6/0x20 · F2 6/0x40 · F3 6/0x80 · F4 7/0x01 · F5 7/0x02 · RET 7/0x80 · ESC 7/0x04
proc tap {row mask {dur 0.12}} { keymatrixdown $row $mask; after time $dur "keymatrixup $row $mask" }
proc hold {row mask dur} { keymatrixdown $row $mask; after time $dur "keymatrixup $row $mask" }
proc at {t script} { after time $t $script }
