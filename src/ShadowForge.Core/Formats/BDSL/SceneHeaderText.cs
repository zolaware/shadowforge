using System.Numerics;
using System.Text;
using ShadowForge.Scene;
using static ShadowForge.Formats.BDSL.SceneTextSyntax;

namespace ShadowForge.Formats.BDSL;

/// <summary>
/// The scene "name" { key = value ... } block.
/// </summary>
internal static class SceneHeaderText
{
    /// <summary>
    /// Reads the block whose header is at line i and returns the index of the line after it.
    /// </summary>
    public static int Read(SceneFile scene, string[] lines, int i)
    {
        var tokens = TextHelper.TokenizeLine(Clean(lines[i]));
        if (tokens.Count >= 2)
            scene.SceneName = TextHelper.UnquoteString(tokens[1]);

        for (i++; i < lines.Length; i++)
        {
            string line = Clean(lines[i]);
            if (IsBlockEnd(line)) return i + 1;
            if (TrySplitField(line, out string key, out string value))
                ApplyField(scene, key, value);
        }
        return i;
    }

    private static void ApplyField(SceneFile scene, string key, string value)
    {
        switch (key)
        {
            case "version":
                scene.Version = TextHelper.UnquoteString(value);
                break;
            case "filename":
                scene.Filename = TextHelper.UnquoteString(value);
                break;
            case "flags":
                scene.Flags = TextHelper.ParseInteger(value);
                break;
            case "area":
                scene.AreaType = TextHelper.ParseAreaType(value);
                break;
            case "messages":
                scene.MessagePath = TextHelper.UnquoteString(value);
                break;
            case "stage_id":
                scene.StageId = TextHelper.ParseInteger(value);
                break;
            case "area_metadata":
                scene.AreaMetadata = TextHelper.ParseInteger(value);
                break;
            case "build_path":
                scene.BuildPath = TextHelper.UnquoteString(value);
                break;
            case "scene_version":
                scene.SceneVersion = TextHelper.ParseInteger(value);
                break;
            case "area_origin":
                var origin = ParseVector(value);
                scene.AreaOriginX = origin.X;
                scene.AreaOriginY = origin.Y;
                scene.AreaOriginZ = origin.Z;
                break;
            case "area_origin_w":
                scene.AreaOriginW = TextHelper.ParseInteger(value);
                break;
            case "area_config":
                var cfg = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (cfg.Length > 0) scene.AreaConfig0 = TextHelper.ParseInteger(cfg[0]);
                if (cfg.Length > 1) scene.AreaConfig1 = TextHelper.ParseInteger(cfg[1]);
                if (cfg.Length > 2) scene.AreaConfig2 = TextHelper.ParseInteger(cfg[2]);
                if (cfg.Length > 3) scene.AreaConfig3 = TextHelper.ParseInteger(cfg[3]);
                if (cfg.Length > 4) scene.AreaConfig4 = TextHelper.ParseInteger(cfg[4]);
                break;
        }
    }

    public static void Write(StringBuilder sb, SceneFile scene)
    {
        sb.AppendLine($"scene \"{TextHelper.EscapeString(scene.SceneName)}\" {{");
        sb.AppendLine($"    version = \"{scene.Version}\"");

        if (!string.IsNullOrEmpty(scene.Filename))
            sb.AppendLine($"    filename = \"{TextHelper.EscapeString(scene.Filename)}\"");

        if (scene.Flags != 0)
            sb.AppendLine($"    flags = {scene.Flags}");

        sb.AppendLine($"    area = {TextHelper.AreaTypeName(scene.AreaType)}");

        if (scene.StageId != 0)
            sb.AppendLine($"    stage_id = {scene.StageId}");

        if (scene.AreaMetadata != 0)
            sb.AppendLine($"    area_metadata = {scene.AreaMetadata}");

        if (scene.MessagePath.Length > 0)
            sb.AppendLine($"    messages = \"{TextHelper.EscapeString(scene.MessagePath)}\"");

        if (scene.BuildPath.Length > 0)
            sb.AppendLine($"    build_path = \"{TextHelper.EscapeString(scene.BuildPath)}\"");

        var origin = new Vector3(scene.AreaOriginX, scene.AreaOriginY, scene.AreaOriginZ);
        if (!IsZero(origin))
            sb.AppendLine($"    area_origin = {FormatVector(origin)}");

        if (scene.SceneVersion != 0)
            sb.AppendLine($"    scene_version = 0x{scene.SceneVersion:X}");

        if (scene.AreaOriginW != 0)
            sb.AppendLine($"    area_origin_w = 0x{scene.AreaOriginW:X}");

        if (scene.AreaConfig0 != 0 || scene.AreaConfig1 != 0 || scene.AreaConfig2 != 0 || scene.AreaConfig3 != 0 || scene.AreaConfig4 != 0)
            sb.AppendLine($"    area_config = 0x{scene.AreaConfig0:X} 0x{scene.AreaConfig1:X} 0x{scene.AreaConfig2:X} 0x{scene.AreaConfig3:X} 0x{scene.AreaConfig4:X}");

        sb.AppendLine("}");
    }
}
