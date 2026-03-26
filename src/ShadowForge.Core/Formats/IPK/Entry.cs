// P:\shadowforge\src\ShadowForge.Core\Formats\IPK\Entry.cs
namespace ShadowForge.Formats.IPK;

public class Entry
{
    public string Name { get; set; } = "";
    public bool IsCompressed { get; set; }
    public uint CompressedSize { get; set; }
    public uint Offset { get; set; }
    public uint OriginalSize { get; set; }
}
