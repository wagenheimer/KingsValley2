set ::dumpdir [format "mus/s%02d" $::stage]
source stage.tcl
at 28.4 {
  if {[catch { vgm_rec prefix [format "kv2_st%02d" $::stage]; set ::vgm::directory "[pwd]/vgm"; ::vgm::set_next_filename; vgm_rec start PSG SCC } err]} { log "ERR $err" }
  if {[catch { record start -audioonly -doublesize [format "[pwd]/wav/kv2_st%02d.wav" $::stage] } err]} { log "ERRWAV $err" } else { log "wav $err" }
}
at 80 { catch { vgm_rec stop }; catch { record stop }; exit }
