namespace ShadowForge.Formats.XACT;

/// <summary>
/// One cue: the name the game plays it by, and the wave it resolves to.
/// </summary>
public sealed class SoundCue
{
    public int Index { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Index into the wave bank named by <see cref="WaveBankIndex"/>.</summary>
    public int WaveIndex { get; set; }

    public int WaveBankIndex { get; set; }

    /// <summary>
    /// File offset of the wave index, which the wave bank index byte follows. These
    /// <see cref="SoundBank.WaveReferenceSize"/> bytes pick the wave the cue plays.
    /// </summary>
    public int WaveReferenceOffset { get; set; }

    /// <summary>
    /// True when the sound record carries clips instead of its own wave reference. Every
    /// Blue Dragon cue still resolves to one wave, taken from the first clip.
    /// </summary>
    public bool IsComplex { get; set; }

    /// <summary>Clip count for a complex sound, zero for a simple one.</summary>
    public int ClipCount { get; set; }
}
