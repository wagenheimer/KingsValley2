using KingsValley.Core.Common;
using KingsValley.Core.Gameplay;
using KingsValley.Engine.Extraction;
using Xunit;

namespace KingsValley.Core.Tests;

public class NativeDeathTimingTests
{
    [Fact]
    public void StartingRestCountsSparesAndDoesNotAddAnExtraDeathHold()
    {
        Assert.Equal(0, GameConstants.StartingRest);
        Assert.True(GameRun.New(0).Died().IsGameOver);
        int animation = PlayerPoses.DeathSpinFrames * PlayerPoses.DeathSpins * PlayerPoses.DeathFrameTicks + PlayerPoses.DeathFinalTicks;
        Assert.Equal(101, animation + NativeDeathTiming.DecisionTicks(0));
        Assert.Equal(223, animation + NativeDeathTiming.DecisionTicks(1) + NativeDeathTiming.RetryBannerTicks);
    }

    [LocalRomFact("msx1-frs")]
    public void FrsNewGameRestIsDecrementedBeforeTheFirstStage()
    {
        byte[] rom = File.ReadAllBytes(Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_FRS_ROM")!);
        var machine = CartridgeMachines.Create(rom);
        Assert.True(machine.Boot(2_000_000));
        machine.CallAddress(0x4C02, 200_000);
        Assert.Equal(1, machine.Memory(0xE240));
        machine.CallRoutine(0x48AF, bc: 0, maxInstructions: 2_000_000);
        Assert.Equal(GameConstants.StartingRest, machine.Memory(0xE240));
    }

    [LocalRomFact("msx1-frs")]
    public void FrsNativeDeathTransitionsMatchGameOverAndRetryTiming()
    {
        byte[] rom = File.ReadAllBytes(Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_FRS_ROM")!);
        foreach (int rest in new[] { 0, 1 })
        {
            var machine = RomScreens.BootToGameplay(rom);
            machine.Write(0xE240, (byte)rest);
            // A943 / 9018 native death initialization, written without disturbing
            // the FRS main CPU continuation (calling a routine would lose it).
            machine.Write(0xE280, 5); machine.Write(0xE285, 0); machine.Write(0xE295, 0);
            machine.Write(0xE296, 3); machine.Write(0xE298, 1); machine.Write(0xE215, 0);
            int previousClock = machine.Memory(0xE203), ticks = 0, transition = -1;
            for (int frame = 0; frame < 1200; frame++)
            {
                machine.Frame(); int clock = machine.Memory(0xE203);
                ticks += (clock - previousClock) & 255; previousClock = clock;
                if (ticks <= 100) continue;
                int targetMode = rest == 0 ? 7 : 5;
                if (machine.Memory(0xE200) != targetMode) continue;
                transition = ticks; break;
            }
            Assert.Equal(rest == 0 ? 101 : 223, transition);
            Assert.Equal(0, machine.Memory(0xE240));
        }
    }
}
