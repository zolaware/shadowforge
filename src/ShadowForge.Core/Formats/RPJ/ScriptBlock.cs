namespace ShadowForge.Formats.RPJ;

public class ScriptBlock
{
    public int OwnerEntryIndex { get; set; }
    public int FileOffset { get; set; }
    public int FileSize { get; set; }
    public byte[] HeaderData { get; set; } = new byte[0x100];
    public List<ScriptElement> Elements { get; set; } = new();
    public byte[] ParamData { get; set; } = Array.Empty<byte>();
    public ScriptHeader Header => new(HeaderData);
}

public abstract class ScriptElement { }

public class ScriptInstruction : ScriptElement
{
    public uint Opcode { get; set; }
    public uint Size { get; set; }
    public uint[] RawParams { get; set; } = Array.Empty<uint>();
}

public class ScriptData : ScriptElement
{
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
}
