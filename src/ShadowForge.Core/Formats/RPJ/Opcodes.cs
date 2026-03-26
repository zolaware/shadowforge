namespace ShadowForge.Formats.RPJ;

public static class Opcodes
{
    public const uint LabelMarker = 5000;
    public const uint OpcodeMin = 5001;
    public const uint OpcodeMax = 5100;

    private static readonly Dictionary<uint, string> Names = new()
    {
        [5000] = "label",
        [5001] = "show_message",
        [5003] = "set_variable",
        [5004] = "give_item",
        [5005] = "give_item2",
        [5006] = "set_animation",
        [5008] = "battle_magic",
        [5010] = "set_state",
        [5012] = "if_extended",
        [5013] = "if_extended2",
        [5014] = "if_extended3",
        [5016] = "gosub",
        [5017] = "play_bgm",
        [5018] = "play_se",
        [5020] = "wait",
        [5021] = "map_change",
        [5023] = "end_script",
        [5024] = "character_setup",
        [5025] = "shop_inn",
        [5026] = "screen_fade",
        [5027] = "npc_action",
        [5028] = "move_character",
        [5029] = "move_character2",
        [5031] = "fade_transition",
        [5032] = "transition2",
        [5033] = "transition3",
        [5034] = "formation",
        [5035] = "party_gather",
        [5036] = "dismiss_party",
        [5037] = "animation_play",
        [5039] = "character_state",
        [5040] = "character_state2",
        [5041] = "character_state3",
        [5042] = "if",
        [5043] = "goto_label",
        [5044] = "set_flag",
        [5045] = "close_dialogue",
        [5046] = "camera_setup",
        [5047] = "camera_setup2",
        [5048] = "camera_setup3",
        [5049] = "camera_setup4",
        [5051] = "effect",
        [5052] = "effect2",
        [5053] = "effect3",
        [5054] = "effect4",
        [5055] = "effect5",
        [5056] = "effect6",
        [5057] = "render_state",
        [5059] = "render_state2",
        [5060] = "render_state3",
        [5061] = "render_state4",
        [5062] = "camera_control",
        [5063] = "flag_get_set",
        [5064] = "flag_op2",
        [5065] = "flag_op3",
        [5066] = "flag_op4",
        [5067] = "show_message2",
        [5068] = "shop_inn2",
        [5070] = "fade_transition2",
        [5071] = "character_effect",
        [5072] = "set_abilities",
        [5073] = "ability_op2",
        [5074] = "ability_op3",
        [5076] = "ability_op4",
        [5077] = "ability_op5",
        [5078] = "ability_op6",
        [5079] = "face_target",
        [5080] = "face_target2",
        [5081] = "face_target3",
        [5083] = "visual_effect",
        [5084] = "quest_patch",
        [5085] = "quest_op2",
        [5086] = "quest_op3",
        [5087] = "visual_effect2",
        [5088] = "visual_effect3",
        [5089] = "visual_effect4",
        [5090] = "visual_effect5",
        [5091] = "visual_effect6",
        [5093] = "special_op",
        [5094] = "special_op2",
        [5095] = "special_op3",
        [5096] = "if_extended4",
        [5097] = "set_variable2",
        [5098] = "special_op4",
        [5099] = "special_op5",
        [5100] = "special_op6",
    };

    private static readonly Lazy<Dictionary<string, uint>> ReverseNames = new(() =>
    {
        var dict = new Dictionary<string, uint>(Names.Count);
        foreach (var kvp in Names)
            dict[kvp.Value] = kvp.Key;
        return dict;
    });

    public static string GetName(uint opcode)
    {
        if (Names.TryGetValue(opcode, out var name))
            return name;
        return $"op_{opcode}";
    }

    public static bool TryGetOpcode(string name, out uint opcode)
    {
        if (ReverseNames.Value.TryGetValue(name, out opcode))
            return true;

        if (name.StartsWith("op_") && uint.TryParse(name[3..], out opcode))
            return true;

        opcode = 0;
        return false;
    }

    public static bool IsValid(uint opcode) => opcode >= LabelMarker && opcode <= OpcodeMax;
}
