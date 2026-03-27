using ShadowForge.IO;

namespace ShadowForge.Formats.RPJ;

/// <summary>
/// Decoded view of the 0x100-byte script block header.
///
/// Layout (IDA-verified from sub_821AD868):
///   +0x00  u32   chapter_min       (-1 = unconditional, else chapter range start)
///   +0x04  u32   chapter_max       (chapter range end, inclusive)
///   +0x08  Condition[8]            8 condition slots, 16 bytes each
///   +0x88  u32[2]                  unknown / always zero (genuinely reserved)
///   +0x90  u32   auto_run_flags
///   +0x94  u32   encounter_mode
///   +0x98  f32   encounter_range
///   +0x9C  u32   auto_set_var_flag
///   +0xA0  u32   auto_set_var_index
///   +0xA4  u32[8]                  unknown / always zero (genuinely reserved)
///   +0xC4  u32   linked_entry_id
///   +0xC8  u32   spawn_config
///   +0xCC  f32   spawn_angle
///   +0xD0  f32   spawn_scale
///   +0xD4  u32   render_flags
///   +0xD8  i32   behavior_mode
///   +0xDC  f32   interaction_radius
///   +0xE0  u32   block_type
///   +0xE4  u32   type_data
///   +0xE8  u32   spawn_mode
///   +0xEC  u32   bytecode_size
///   +0xF0  u32   param_count       (param data size in bytes, runtime divides by 4)
///   +0xF4  u32   reserved
///   +0xF8  u32   param_ptr         (relocated at runtime to point at param data)
///   +0xFC  u32   next_block        (relative offset to next block, 0 = end of chain)
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

    private uint SafeRead(int offset)
        => _data.Length >= offset + 4 ? BigEndian.ReadUInt32(_data, offset) : 0;

    private float SafeReadFloat(int offset)
        => _data.Length >= offset + 4 ? BigEndian.ReadFloat(_data, offset) : 0f;

    private int SafeReadInt(int offset)
        => _data.Length >= offset + 4 ? BigEndian.ReadInt32(_data, offset) : 0;

    public uint ChapterMin => SafeRead(0x00);
    public uint ChapterMax => SafeRead(0x04);
    public bool IsUnconditional => ChapterMin == 0xFFFFFFFF;

    public uint BytecodeSize => SafeRead(0xEC);
    public uint ParamCount => SafeRead(0xF0);
    public uint NextBlockOffset => SafeRead(0xFC);

    // Semantic fields at +0x90 through +0xEB
    public uint AutoRunFlags => SafeRead(0x90);
    public uint EncounterMode => SafeRead(0x94);
    public float EncounterRange => SafeReadFloat(0x98);
    public uint AutoSetVarFlag => SafeRead(0x9C);
    public uint AutoSetVarIndex => SafeRead(0xA0);
    public uint LinkedEntryId => SafeRead(0xC4);
    public uint SpawnConfig => SafeRead(0xC8);
    public float SpawnAngle => SafeReadFloat(0xCC);
    public float SpawnScale => SafeReadFloat(0xD0);
    public uint RenderFlags => SafeRead(0xD4);
    public int BehaviorMode => SafeReadInt(0xD8);
    public float InteractionRadius => SafeReadFloat(0xDC);
    public uint BlockType => SafeRead(0xE0);
    public uint TypeData => SafeRead(0xE4);
    public uint SpawnMode => SafeRead(0xE8);

    public byte[] RawBytes => _data;

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

    /// <summary>True if the genuinely unknown regions (+0x88..+0x8F and +0xA4..+0xC3) are all zero.</summary>
    public bool UnknownRegionsAreZero
    {
        get
        {
            if (_data.Length < 0xEC) return true;
            for (int i = 0x88; i < 0x90; i++)
                if (_data[i] != 0) return false;
            for (int i = 0xA4; i < 0xC4; i++)
                if (_data[i] != 0) return false;
            return true;
        }
    }

    /// <summary>True if any byte in the semantic metadata region +0x90..+0xEB is non-zero.</summary>
    public bool HasAnyMetadata
    {
        get
        {
            if (_data.Length < 0xEC) return false;
            for (int i = 0x90; i < 0xEC; i++)
                if (_data[i] != 0) return true;
            return false;
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
