namespace KingsValley.Core.Gameplay;

/// <summary>
/// Original sound IDs (the number passed to routine <c>4326</c> by the trampolines <c>41E0..430B</c>
/// of bank 0). Each constant says who calls it in the ROM — it is the same number as the extracted files.
/// </summary>
public static class OriginalSounds
{
    /// <summary>Last stone: the door released (99E8 → 4212).</summary>
    public const int DoorUnlocked = 0x0B;

    /// <summary>Jump (A03D → 423A).</summary>
    public const int Jump = 0x13;

    /// <summary>Mummy lands (B54E → 423F).</summary>
    public const int EnemyLanded = 0x14;

    /// <summary>Digging with the shovel, every 4 ticks (A40A → 4244).</summary>
    public const int DigShovel = 0x15;

    /// <summary>Digging with the drill (A428 → 4249).</summary>
    public const int DigDrill = 0x16;

    /// <summary>Digging with the pickaxe (A414 → 424E).</summary>
    public const int DigPickaxe = 0x17;

    /// <summary>Digging with the hammer (A41E → 4253).</summary>
    public const int DigHammer = 0x18;

    /// <summary>Pegou item (9AA7 → 4258).</summary>
    public const int PickItem = 0x19;

    /// <summary>Picked up soul stone (99F8 → 425D).</summary>
    public const int PickSoulStone = 0x1A;

    /// <summary>Boomerang spinning in flight: whir at the end of the 6-step animation cycle (A99D → 4262).</summary>
    public const int BoomerangSpin = 0x1B;

    /// <summary>Knife spinning in flight: whir at the end of the 3-step animation cycle (A87F → 4267).</summary>
    public const int KnifeSpin = 0x1C;

    /// <summary>Mummy is born in the sarcophagus (AD4D → 4271).</summary>
    public const int EnemySpawn = 0x1E;

    /// <summary>Type 3 appears in the cloud (B291 → 426C).</summary>
    public const int GuardSpawn = 0x1D;

    /// <summary>Type 3 hops (B37C → 4276).</summary>
    public const int GuardHop = 0x1F;

    /// <summary>The stone (type 4) hits and turns into a block (B7C6 → 428F).</summary>
    public const int RockStop = 0x24;

    /// <summary>Entered the door (8FFD → 428A).</summary>
    public const int DoorEnter = 0x23;

    /// <summary>Sarcophagus closes (AE6A → 4294).</summary>
    public const int CoffinClose = 0x25;

    /// <summary>Knife/boomerang hit an enemy (9B7C → 4299).</summary>
    public const int EnemyHit = 0x26;

    /// <summary>Vick atingido (9AFB → 42A3).</summary>
    public const int PlayerHit = 0x28;

    /// <summary>End of the death spin (A348 → 42A7).</summary>
    public const int PlayerDeathJingle = 0x29;

    /// <summary>Mummy starts to fall (AF48 → 42B3).</summary>
    public const int EnemyFall = 0x2C;

    /// <summary>Mummy vanishes in sparks (660E → 42B7).</summary>
    public const int EnemyVanish = 0x2D;

    /// <summary>Extra life by score: the "card" that rises from the screen (42AB).</summary>
    public const int ExtraLifeByScore = 0x2A;

    /// <summary>The knife LANDED and became a ground item on this frame (A76E/A9E0 → 42DB). On impact it does not play.</summary>
    public const int KnifeStop = 0x36;

    /// <summary>Vick caught the boomerang back with empty hands (A97C <c>boom_vic</c> → 42D7).</summary>
    public const int BoomerangCatch = 0x35;

    /// <summary>Vick lands after jumping/falling (A1BE/A312 → 42E3).</summary>
    public const int PlayerLand = 0x38;

    /// <summary>Vick starts to fall (A005 → 42F3).</summary>
    public const int PlayerFall = 0x3C;

    /// <summary>
    /// Trap (E600 type 4, <c>tick_trap</c> 0xBD21): the 4-cell bar is <b>floor</b>. Vick on
    /// top of it arms it; stepping off the top while standing, 2 ticks later the <c>trap_punch</c> (0xBD7A)
    /// plays this sound and the <c>trap_erase</c> (0xBD65) zeroes the 4 map cells.
    /// </summary>
    public const int Trap = 0x27;

    /// <summary>A triggered falling-wall column stamps another native terrain cell.</summary>
    public const int FallingWall = 0x31;

    /// <summary>
    /// Step of the push door (E600 type 1, the <c>tick_coffin</c> of the disassembly): the ROM calls
    /// <c>sfx_21</c> (0xBAC4 → 4280) when the counter <c>IX+6</c> reaches zero and the phase <c>IX+5</c> advances —
    /// once per bar step, when pushing right and left.
    /// </summary>
    public const int PushDoor = 0x21;

    /// <summary>Sarcophagus opens and the mummy appears (AE3F → 4307).</summary>
    public const int CoffinOpen = 0x41;
}
