# Grava audio (WAV) de um som/musica ::id por ::dur segundos, na tela de senha.
# Uso: openmsx -machine C-BIOS_MSX2_JP -cart rom/kv2_msx1.rom -script rec.tcl
set ::id 5
set ::dur 40
set ::out "rec/kv2_rec"
if {![info exists ::dumpdir]} { set ::dumpdir rec }
file mkdir $::dumpdir
source lib.tcl

proc log {msg} { set f [open "$::dumpdir/log.txt" a]; puts $f $msg; close $f }
proc inject {{id -1}} {
  if {$id < 0} { set id $::id }
  set sp [expr {[reg sp] - 2}]
  reg sp $sp
  poke16 $sp [reg pc]
  reg pc 0x432e
  reg a $id
}

at 12 {tap 8 1}
at 16 {tap 8 1}
at 19 {tap 8 0x40}
at 20.5 {tap 8 1}
at 22.5 { inject 1 }
at 23.0 {
  set name [format "%s%02d" $::out $::id]
  catch { record start -audioonly "${name}.wav" } e
  log "$e"
  inject
}
at [expr {23.0 + $::dur}] { catch { record stop }; exit }
