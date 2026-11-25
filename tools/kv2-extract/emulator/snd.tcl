# Toca o som/musica ::id injetando CALL 0x432E na tela de titulo e grava WAV + VGM.
set ::dumpdir [format "snd/id%02d" $::id]
source lib.tcl
at 12 {tap 8 1}
at 16 {tap 8 1}
at 19 {tap 8 0x40}
at 20.5 {tap 8 1}
proc log {msg} { set f [open "$::dumpdir/log.txt" a]; puts $f $msg; close $f }
proc inject {} {
  set sp [expr {[reg sp] - 2}]
  reg sp $sp
  poke16 $sp [reg pc]
  reg pc 0x432e
  reg a $::id
}
at 23.0 {
  catch { vgm_rec prefix [format "kv2_id%02d" $::id]; set ::vgm::directory "[pwd]/vgm_id"; ::vgm::set_next_filename; vgm_rec start PSG SCC } e1
  catch { record start -audioonly [format "[pwd]/wav_id/kv2_id%02d.wav" $::id] } e2
  log "$e1 | $e2"
  inject
}
at [expr {23.0 + $::dur}] { catch { vgm_rec stop }; catch { record stop }; exit }
