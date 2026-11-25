# Entra direto num stage pela senha. Uso: defina ::stage e ::dumpdir antes de "source stage.tcl".
source lib.tcl
set ::rb "King's Valley 2 - The Seal Of El Giza - MSX1 Version romblocks"
proc pw {stage {lives 99}} {
  set l0 [expr {$lives % 10}]; set l1 [expr {$lives / 10}]
  set s0 [expr {$stage % 16}]; set s1 [expr {$stage / 16}]
  return [format "%c%c%c%cAMCN" [expr {65+$l0}] [expr {65+$s1}] [expr {65+$s0}] [expr {65+$l1}]]
}
proc log {msg} { set f [open "$::dumpdir/log.txt" a]; puts $f $msg; close $f }
at 12 {tap 8 1}
at 16 {tap 8 1}
at 19 {tap 8 0x40}
at 20.5 {tap 8 1}
at 22 {tap 8 0x10}
set ::pwd [pw $::stage]
for {set k 0} {$k < 8} {incr k} { at [expr {23 + $k*0.6}] "type [string index $::pwd $k]" }
at 28.5 {tap 7 0x80}
