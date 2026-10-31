using ShadowForge.IO;

namespace ShadowForge.Formats.ETL;

/// <summary>
/// One enemy of a formation. Enemy packs the stat table in its high half (0 enemy_zako,
/// 1 enemy_boss) and the table row in its low half. Param is the word after it, the same in
/// every record of one enemy row; its meaning is unknown. Slot is the enemy's place in the
/// formation.
/// </summary>
public readonly record struct Spawn(uint Enemy, uint Param, uint Slot)
{
    public int Table => (int)(Enemy >> 16);

    public int Row => (int)(Enemy & 0xFFFF);

    public override string ToString() => Table switch
    {
        0 => $"zako {Row}",
        1 => $"boss {Row}",
        _ => $"table {Table} row {Row}",
    };
}

/// <summary>
/// One encounter group: its weight word and the enemies it fields.
/// </summary>
public sealed record Formation(uint Weight, IReadOnlyList<Spawn> Spawns);

/// <summary>
/// A map's enemy placement table (.etl), big-endian. A 16-byte header holds the formation count
/// and the offset of the formation table. Each 16-byte formation record holds the offset of its
/// first spawn, the offset of the next formation record (0 on the last) and the weight. Spawn
/// records are 32 bytes: enemy, param, a pad word, slot, and the offset of the next spawn in the
/// formation counted from the first spawn record (0 on the last). A formation's spawns run from
/// its first record up to the next formation's. A table with no formations is 16 zero bytes.
/// </summary>
public static class EnemyPlacement
{
    public const int HeaderSize = 0x10;
    public const int FormationRecordSize = 0x10;
    public const int SpawnRecordSize = 0x20;

    public static List<Formation> Read(byte[] data)
    {
        var formations = new List<Formation>();
        if (data.Length < HeaderSize) return formations;

        int count = (int)BigEndian.ReadUInt32(data, 0x00);
        int table = (int)BigEndian.ReadUInt32(data, 0x04);
        if (count == 0) return formations;
        if (table + (long)count * FormationRecordSize > data.Length)
            throw new InvalidDataException($"enemy placement table: {count} formations run past the end of the file");

        var firstSpawn = new int[count + 1];
        var weights = new uint[count];
        for (int i = 0; i < count; i++)
        {
            int record = table + i * FormationRecordSize;
            firstSpawn[i] = (int)BigEndian.ReadUInt32(data, record);
            weights[i] = BigEndian.ReadUInt32(data, record + 0x08);
        }
        firstSpawn[count] = data.Length;

        for (int i = 0; i < count; i++)
        {
            var spawns = new List<Spawn>();
            int end = Math.Min(firstSpawn[i + 1], data.Length);
            for (int at = firstSpawn[i]; at + SpawnRecordSize <= end; at += SpawnRecordSize)
                spawns.Add(new Spawn(
                    BigEndian.ReadUInt32(data, at),
                    BigEndian.ReadUInt32(data, at + 0x04),
                    BigEndian.ReadUInt32(data, at + 0x0C)));
            formations.Add(new Formation(weights[i], spawns));
        }
        return formations;
    }

    /// <summary>
    /// Lays the table out the way the shipped files are: header, formation records, then every
    /// formation's spawn records in order.
    /// </summary>
    public static byte[] Write(IReadOnlyList<Formation> formations)
    {
        int count = formations.Count;
        int spawnStart = HeaderSize + count * FormationRecordSize;
        var output = new byte[spawnStart + formations.Sum(f => f.Spawns.Count) * SpawnRecordSize];
        if (count == 0) return output;

        BigEndian.WriteUInt32(output, 0x00, (uint)count);
        BigEndian.WriteUInt32(output, 0x04, HeaderSize);

        int at = spawnStart;
        for (int i = 0; i < count; i++)
        {
            int record = HeaderSize + i * FormationRecordSize;
            BigEndian.WriteUInt32(output, record, (uint)at);
            BigEndian.WriteUInt32(output, record + 0x04, i + 1 < count ? (uint)(record + FormationRecordSize) : 0);
            BigEndian.WriteUInt32(output, record + 0x08, formations[i].Weight);

            var spawns = formations[i].Spawns;
            for (int j = 0; j < spawns.Count; j++, at += SpawnRecordSize)
            {
                BigEndian.WriteUInt32(output, at, spawns[j].Enemy);
                BigEndian.WriteUInt32(output, at + 0x04, spawns[j].Param);
                BigEndian.WriteUInt32(output, at + 0x0C, spawns[j].Slot);
                if (j + 1 < spawns.Count)
                    BigEndian.WriteUInt32(output, at + 0x10, (uint)(at + SpawnRecordSize - spawnStart));
            }
        }
        return output;
    }
}
