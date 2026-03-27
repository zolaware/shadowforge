using ShadowForge.IO;

namespace ShadowForge.Formats.RPJ;

/// <summary>
/// Decoded view of the 0x100-byte script block header.
///
/// Layout (IDA-verified from sub_821AD868):
///   +0x00  u32   chapter_min     (-1 = unconditional, else chapter range start)
///   +0x04  u32   chapter_max     (chapter range end, inclusive)
///   +0x08  Condition[8]          8 condition slots, 16 bytes each
///   +0x88  u32[25]               reserved / unused (always zero in files)
///   +0xEC  u32   bytecode_size
///   +0xF0  u32   param_count     (param data size in bytes, runtime divides by 4)
///   +0xF4  u32   reserved
///   +0xF8  u32   param_ptr       (relocated at runtime to point at param data)
///   +0xFC  u32   next_block      (relative offset to next block, 0 = end of chain)
///
/// Each Condition (16 bytes):
///   +0x00  u32   type     (0=none, 2=variable, 3=item_count, 4=party_check, 5=flag)
///   +0x04  u32   operand  (variable index, item ID, party bitmask, flag group)
///   +0x08  u32   op       (0=eq, 1=ge, 2=le, 3=gt, 4=lt, 5=ne)
///   +0x0C  u32   value    (comparison value)
/// </summary>
public readonly struct ScriptHeader
{
    public const int Size = 0x100;
    public const int ConditionCount = 8;
    public const int ConditionSize = 16;
    public const int ConditionsOffset = 0x08;

    private readonly byte[] _data;

    public ScriptHeader(byte[] headerData)
    {
        _data = headerData;
    }

    public uint ChapterMin => BigEndian.ReadUInt32(_data, 0x00);
    public uint ChapterMax => BigEndian.ReadUInt32(_data, 0x04);
    public bool IsUnconditional => ChapterMin == 0xFFFFFFFF;

    public uint BytecodeSize => _data.Length >= 0xF0 ? BigEndian.ReadUInt32(_data, 0xEC) : 0;
    public uint ParamCount => _data.Length >= 0xF4 ? BigEndian.ReadUInt32(_data, 0xF0) : 0;
    public uint NextBlockOffset => _data.Length >= 0x100 ? BigEndian.ReadUInt32(_data, 0xFC) : 0;

    public Condition GetCondition(int index)
    {
        int offset = ConditionsOffset + index * ConditionSize;
        return new Condition(
            BigEndian.ReadUInt32(_data, offset),
            BigEndian.ReadUInt32(_data, offset + 4),
            BigEndian.ReadUInt32(_data, offset + 8),
            BigEndian.ReadUInt32(_data, offset + 12)
        );
    }

    /// <summary>True if all conditions are type=0 (empty).</summary>
    public bool HasNoConditions
    {
        get
        {
            for (int i = 0; i < ConditionCount; i++)
                if (GetCondition(i).Type != 0) return false;
            return true;
        }
    }

    /// <summary>True if +0x88 through +0xEB are all zero.</summary>
    public bool ReservedIsZero
    {
        get
        {
            if (_data.Length < 0xEC) return true;
            for (int i = 0x88; i < 0xEC; i++)
                if (_data[i] != 0) return false;
            return true;
        }
    }

    /// <summary>Returns the reserved region +0x88 to +0xEB if any byte is non-zero.</summary>
    public byte[]? ReservedRegion
    {
        get
        {
            if (ReservedIsZero) return null;
            return _data[0x88..0xEC];
        }
    }
}

public readonly record struct Condition(uint Type, uint Operand, uint Op, uint Value)
{
    public bool IsEmpty => Type == 0;

    public string TypeName => Type switch
    {
        0 => "none",
        2 => "variable",
        3 => "item_count",
        4 => "party_check",
        5 => "flag",
        _ => $"unk_{Type}",
    };

    public string OpName => Op switch
    {
        0 => "==",
        1 => ">=",
        2 => "<=",
        3 => ">",
        4 => "<",
        5 => "!=",
        _ => $"op_{Op}",
    };
}
