# Grava so o VGM de um som/musica ::id por ::dur segundos (sem WAV).
# Uso: editar ::id/::dur e rodar com -script recvgm.tcl
set ::id 9
set ::dur 300
set ::dumpdir vgm_long
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
  catch {
    vgm_rec prefix [format "long_id%02d" $::id]
    set ::vgm::directory "[pwd]/vgm_long"
    ::vgm::set_next_filename
    vgm_rec start PSG SCC
  } e
  log "$e"
  inject
}
at [expr {23.0 + $::dur}] { catch { vgm_rec stop }; exit }
