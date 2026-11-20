# Grava a musica de um stage da ROM MSX2 (RC-761) em VGM.
# Entra pela senha e grava depois que o stage carrega.
set ::stage 1
set ::dur 120
set ::dumpdir msx2mus
file mkdir $::dumpdir
file mkdir vgm_msx2
source stage2.tcl

at 32.0 {
  catch {
    vgm_rec prefix [format "msx2_st%02d" $::stage]
    set ::vgm::directory "[pwd]/vgm_msx2"
    ::vgm::set_next_filename
    vgm_rec start PSG SCC
  } e
  lg "rec $e"
}
at [expr {32.0 + $::dur}] { catch { vgm_rec stop }; exit }
