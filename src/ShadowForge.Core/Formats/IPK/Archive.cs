/**
 * @file        Formats/IPK/Archive.cs
 * @brief       IPK archive data model (file list, alignment, metadata)
 *
 * @copyright   Copyright (c) 2026 Tom Clay <tomc@tctechstuff.com>
 *              All rights reserved.
 *
 * @license     BSD 3-Clause License
 *              See LICENSE file in the project root for full license text.
 */

namespace ShadowForge.Formats.IPK;

public class Archive
{
    public uint Alignment { get; set; }
    public uint FileCount { get; set; }
    public uint ArchiveSize { get; set; }
    public IReadOnlyList<Entry> Entries { get; set; } = Array.Empty<Entry>();
    public bool UsesZlib => Alignment == 0x80;
}
