set ::stage 1; set ::dumpdir tr1
source stage2.tcl
set ::f [open "tr1/trace.txt" w]
proc tk {} {
  set s [format "%d" [peek 0xe203]]
  for {set i 0} {$i < 0x20} {incr i} { append s [format " %02x" [peek [expr {0xe500+$i}]]] }
  append s [format " p %d %d %d c %02x%02x" [peek 0xe282] [peek 0xe284] [peek 0xe280] [peek 0xe2c0] [peek 0xe2c1]]
  puts $::f $s
}
at 29 { debug set_watchpoint write_mem 0xe203 {} tk }
at 75 { close $::f; exit }
