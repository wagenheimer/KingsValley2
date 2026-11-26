# Golden ladder consumption

Golden ladders are consumable passage records, not timed effects. Verified Contest `8FD5` and the original disassembly's `BE15` process sixteen four-byte records at `E7C0`: room, Y, X, flags/length. The low five bits contain height, bit 5 is armed, and bits 6–7 hold the remaining uses. Native stage initialization sets the counter to two (`0x80`).

A matching record arms while Vick is climbing. A subsequent `E2A7` climb-exit/landing strobe consumes that arm and one use. Waiting, enemy movement and unarmed strobes do not consume uses. The native hit region is X within 16 pixels of the ladder and Y from 20 pixels above its top across `height * 8 + 24` pixels, using unsigned-byte comparisons. These conditions matter at the top and bottom; a generic crossing counter would differ.

On the second use, native Contest `9036` removes the record, stamps two soft cells at the top and empties the remaining ladder rows. It aligns Vick's Y with `(Y + 3) & 0xF8` and invokes sound `0x40`. The port mutates collision terrain and filters the consumed ladder out of background assembly. MSX1 restores the underlying mural before drawing changed terrain, so a consumed ladder cannot remain baked into its immutable room dump.

Save states include remaining uses and armed flags per record, alongside mutable room cells. Legacy saves without these fields retain the initial two-use state. The native regression executes the actual Contest routine through arming, one use, an unarmed strobe and final consumption, and checks the resulting terrain. Simulation checks exercise real climbing input and restoration before and after consumption.

This implementation is shared by MSX1 and MSX2 gameplay. Native tests verify the initial two-use counter in both original cartridges (stage 9) and the complete arming/consumption/terrain sequence in the verified JoySNES Contest cartridge. An exhaustive per-cartridge movement trace remains separate from these focused checks.

Rendering-cache review also consulted [openMSX VDPVRAM](https://github.com/openMSX/openMSX/blob/master/src/video/VDPVRAM.cc), especially the visible/cache VRAM windows and renderer observer registration. The port uses its existing map-version invalidation after terrain writes; the game-specific consumption rule comes from the cartridge, not from emulator code.
