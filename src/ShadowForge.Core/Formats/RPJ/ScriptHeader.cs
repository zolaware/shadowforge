using ShadowForge.IO;

namespace ShadowForge.Formats.RPJ;

public readonly struct ScriptHeader
{
    private readonly byte[] _data;

    public ScriptHeader(byte[] headerData)
    {
        _data = headerData;
    }

    public uint Sentinel => BigEndian.ReadUInt32(_data, 0x00);
    public uint BytecodeSize => _data.Length >= 0xF0 ? BigEndian.ReadUInt32(_data, 0xEC) : 0;
    public uint ParamDataSize => _data.Length >= 0xF4 ? BigEndian.ReadUInt32(_data, 0xF0) : 0;
    public uint NextBlockOffset => _data.Length >= 0x100 ? BigEndian.ReadUInt32(_data, 0xFC) : 0;

    public bool HasOnlyKnownFields
    {
        get
        {
            if (_data.Length < 0x100) return false;
            for (int i = 4; i < 0xEC; i++)
                if (_data[i] != 0) return false;
            for (int i = 0xF4; i < 0xFC; i++)
                if (_data[i] != 0) return false;
            return true;
        }
    }

    public byte[]? UnknownMiddle
    {
        get
        {
            if (_data.Length < 0xEC) return null;
            var region = _data[0x04..0xEC];
            return region.Any(b => b != 0) ? region : null;
        }
    }

    public byte[]? UnknownTail
    {
        get
        {
            if (_data.Length < 0xFC) return null;
            var region = _data[0xF4..0xFC];
            return region.Any(b => b != 0) ? region : null;
        }
    }
}
