# Entra num stage da ROM MSX2 pela senha. Defina ::stage e ::dumpdir antes.
source lib.tcl
set ::rb "kv2_msx2_scc.rom romblocks"
proc pw {stage {lives 99}} {
  set l0 [expr {$lives % 10}]; set l1 [expr {$lives / 10}]
  set s0 [expr {$stage % 16}]; set s1 [expr {$stage / 16}]
  return [format "%c%c%c%cAMCN" [expr {65+$l0}] [expr {65+$s1}] [expr {65+$s0}] [expr {65+$l1}]]
}
proc lg {msg} { set f [open "$::dumpdir/log.txt" a]; puts $f $msg; close $f }
proc w16 {a} { expr {[peek $a] | ([peek [expr {$a+1}]] << 8)} }
at 12 {tap 8 1}
at 16 {tap 8 1}
at 19 {tap 8 0x40}
at 20.5 {tap 8 1}
at 22 {tap 8 0x10}
set ::pwd [pw $::stage]
for {set k 0} {$k < 8} {incr k} { at [expr {23 + $k*0.6}] "type [string index $::pwd $k]" }
at 28.5 {tap 7 0x80}
