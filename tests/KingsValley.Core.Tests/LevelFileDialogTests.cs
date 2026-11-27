using System.Runtime.InteropServices;
using KingsValley.Engine.Platform;

namespace KingsValley.Core.Tests;

public class LevelFileDialogTests
{
    [Theory]
    [InlineData("GetOpenFileNameW")]
    [InlineData("GetSaveFileNameW")]
    public void WindowsEntryPointsReachNativeValidationWithoutAMarshallingException(string entryPoint)
    {
        if (!OperatingSystem.IsWindows()) return;
        // An invalid structure length is rejected before Windows opens any UI.
        var arguments = new object[] { new LevelFileDialog.OpenFileName { Size = 1 } };
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var method = typeof(LevelFileDialog).GetMethod(entryPoint, flags)!;
        Assert.False((bool)method.Invoke(null, arguments)!);
        var error = typeof(LevelFileDialog).GetMethod("CommDlgExtendedError", flags)!;
        Assert.Equal(1, (int)error.Invoke(null, null)!); // CDERR_STRUCTSIZE
    }

    [Fact]
    public void WindowsDialogStructureMatchesTheNativeAbiAndCanBeMarshalled()
    {
        Assert.Equal(IntPtr.Size == 8 ? 152 : 88, Marshal.SizeOf<LevelFileDialog.OpenFileName>());
        Assert.Equal(IntPtr.Size == 8 ? 48 : 28, Marshal.OffsetOf<LevelFileDialog.OpenFileName>(nameof(LevelFileDialog.OpenFileName.File)).ToInt32());
        using var buffer = new LevelFileDialog.WindowsFileBuffer("My level.kv2level");
        var data = new LevelFileDialog.OpenFileName
        {
            Size = Marshal.SizeOf<LevelFileDialog.OpenFileName>(), File = buffer.Pointer,
            MaximumFile = LevelFileDialog.WindowsFileBuffer.Capacity,
            Filter = "Level files\0*.kv2level;*.json\0\0", Title = "Save Level As"
        };
        IntPtr structure = Marshal.AllocHGlobal(data.Size);
        try
        {
            Marshal.StructureToPtr(data, structure, false);
            try
            {
                var copy = Marshal.PtrToStructure<LevelFileDialog.OpenFileName>(structure);
                Assert.Equal(buffer.Pointer, copy.File); Assert.Equal(data.MaximumFile, copy.MaximumFile);
                Assert.Equal(data.Title, copy.Title);
            }
            finally { Marshal.DestroyStructure<LevelFileDialog.OpenFileName>(structure); }
        }
        finally { Marshal.FreeHGlobal(structure); }
    }

    [Fact]
    public void NativeWritableBufferPreservesUnicodePathsAndTerminatesShorterResults()
    {
        using var buffer = new LevelFileDialog.WindowsFileBuffer("A long suggested file name.kv2level");
        string selected = @"C:\Levels\Cezar's level é 日本.kv2level";
        char[] nativeResult = (selected + '\0').ToCharArray();
        Marshal.Copy(nativeResult, 0, buffer.Pointer, nativeResult.Length);
        Assert.Equal(selected, buffer.Read());
        Marshal.Copy(new[] { 'X', '\0' }, 0, buffer.Pointer, 2);
        Assert.Equal("X", buffer.Read());
        buffer.Dispose(); buffer.Dispose(); Assert.Equal(IntPtr.Zero, buffer.Pointer);
    }
}
