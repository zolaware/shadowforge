namespace ShadowForge.Formats.DDS;

/// <summary>
/// Represents various graphic formats used in Xbox 360 textures.
/// </summary>
public enum GraphicFormat
{
    TextureFormatA8L8 = 0,       // 2 bytes per pixel (Alpha 8, Luminance 8)
    TextureFormatL8 = 1,         // 1 byte per pixel (Luminance 8)
    TextureFormatDxt1 = 2,       // Block compressed, 8 bytes per 4x4 block
    TextureFormatDxt3 = 3,       // Block compressed, 16 bytes per 4x4 block
    TextureFormatDxt5 = 4,       // Block compressed, 16 bytes per 4x4 block
    TextureFormatDxn = 5,        // Block compressed (Normal Map), 16 bytes per 4x4 block (BC5)
    TextureFormatA8R8G8B8 = 6,   // 4 bytes per pixel (Alpha, Red, Green, Blue)
    TextureFormatX4R4G4B4 = 7,   // 2 bytes per pixel (X, Red, Green, Blue - X is unused)
    TextureFormatR5G6B5 = 8      // 2 bytes per pixel (Red 5, Green 6, Blue 5)
}