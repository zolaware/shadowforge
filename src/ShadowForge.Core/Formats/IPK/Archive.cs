// P:\shadowforge\src\ShadowForge.Core\Formats\IPK\Archive.cs
namespace ShadowForge.Formats.IPK;

public class Archive
{
    public uint Alignment { get; set; }
    public uint FileCount { get; set; }
    public uint ArchiveSize { get; set; }
    public IReadOnlyList<Entry> Entries { get; set; } = Array.Empty<Entry>();
    public bool UsesZlib => Alignment == 0x80;
}
