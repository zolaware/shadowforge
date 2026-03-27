/**
 * @file        Formats/IPK/Entry.cs
 * @brief       IPK archive entry metadata (name, size, offset, compression)
 *
 * @copyright   Copyright (c) 2026 Tom Clay <tomc@tctechstuff.com>
 *              All rights reserved.
 *
 * @license     BSD 3-Clause License
 *              See LICENSE file in the project root for full license text.
 */

namespace ShadowForge.Formats.IPK;

public class Entry
{
    public string Name { get; set; } = "";
    public bool IsCompressed { get; set; }
    public uint CompressedSize { get; set; }
    public uint Offset { get; set; }
    public uint OriginalSize { get; set; }
}
