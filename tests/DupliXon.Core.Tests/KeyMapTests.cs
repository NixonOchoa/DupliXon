using DupliXon.Media.Input;

public class KeyMapTests
{
    [Theory]
    [InlineData("KeyA", 0x41)]
    [InlineData("Digit7", 0x37)]
    [InlineData("F5", 0x74)]
    [InlineData("F12", 0x7B)]
    [InlineData("Numpad3", 0x63)]
    [InlineData("ArrowLeft", 0x25)]
    [InlineData("MetaLeft", 0xA2)] // ⌘ acts as Ctrl
    [InlineData("Unknown", 0)]
    public void MapsBrowserCodesToVirtualKeys(string code, int vk) => Assert.Equal(vk, KeyMap.ToVirtualKey(code));
}
