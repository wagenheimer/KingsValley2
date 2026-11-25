# Captura um stage: openmsx ... -script grab.tcl  (com ::stage definido via -command)
set ::dumpdir [format "stages/s%02d" $::stage]
source stage.tcl
set ::hit 0
debug set_bp 0x43d3 {} {
  if {!$::hit} {
    set ::hit 1
    set t [machine_info time]
    foreach d {0.6 1.2 2.0 3.0 4.5} { after time $d "dumpall [format t%03d [expr {int($d*10)}]]" }
    after time 4.6 exit
  }
}
at 70 { log "timeout"; exit }
