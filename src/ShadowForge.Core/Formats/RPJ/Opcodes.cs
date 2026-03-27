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
        [5005] = "give_gold",
        [5006] = "set_animation",
        [5008] = "battle_magic",
        [5010] = "formation",
        [5012] = "if_extended",
        [5013] = "goto_label",
        [5014] = "shop_open",
        [5016] = "gosub",
        [5017] = "play_bgm",
        [5018] = "play_se",
        [5020] = "wait",
        [5021] = "map_change",
        [5022] = "render_state_toggle",
        [5023] = "end_script",
        [5024] = "character_state",
        [5025] = "transition2",
        [5026] = "screen_fade",
        [5027] = "npc_action",
        [5028] = "move_character",
        [5029] = "player_teleport",
        [5030] = "battle_damage",
        [5031] = "fade_transition",
        [5032] = "event_scene",
        [5033] = "character_walk",
        [5034] = "give_medal",
        [5035] = "character_setup",
        [5036] = "dismiss_party",
        [5037] = "character_equip",
        [5039] = "character_position",
        [5040] = "set_field_state",
        [5041] = "character_visible",
        [5042] = "if",
        [5043] = "visual_effect",
        [5044] = "special_op",
        [5045] = "close_dialogue",
        [5046] = "camera_setup",
        [5047] = "save_point",
        [5048] = "render_state",
        [5049] = "camera_mode",
        [5051] = "special_battle",
        [5052] = "camera_target",
        [5053] = "scene_script",
        [5054] = "transition_effect",
        [5055] = "animation_play",
        [5056] = "battle_formation",
        [5057] = "conditional_goto",
        [5059] = "character_effect",
        [5060] = "effect",
        [5061] = "animation_trigger",
        [5062] = "camera_control",
        [5063] = "flag_get_set",
        [5064] = "event_action",
        [5065] = "check_animation",
        [5066] = "shop_inn",
        [5071] = "set_field_script",
        [5072] = "set_abilities",
        [5073] = "lookup_map",
        [5074] = "equip_skill",
        [5075] = "formation_battle",
        [5076] = "character_shadow",
        [5077] = "character_speed",
        [5078] = "npc_walk_speed",
        [5079] = "face_target",
        [5080] = "effect_spawn",
        [5081] = "field_camera",
        [5083] = "party_gather",
        [5084] = "quest_patch",
        [5085] = "set_battle_result",
        [5086] = "give_item_special",
        [5087] = "movie_play",
        [5088] = "save_position",
        [5089] = "load_event_pack",
        [5090] = "npc_walk_state",
        [5091] = "shadow_toggle",
        [5093] = "get_variable",
        [5094] = "select_menu",
        [5095] = "treasure_chest",
        [5098] = "special_query",
        [5099] = "nop",
        [5100] = "scene_autoplay",
    };

    private static readonly Dictionary<uint, uint> AliasToCanonical = new()
    {
        [5067] = 5001,
        [5068] = 5025,
        [5070] = 5031,
        [5096] = 5012,
        [5097] = 5003,
    };

    private static readonly Dictionary<uint, string> AliasNames = new()
    {
        [5067] = "show_message",
        [5068] = "transition2",
        [5070] = "fade_transition",
        [5096] = "if_extended",
        [5097] = "set_variable",
    };

    public static bool IsAlias(uint opcode) => AliasToCanonical.ContainsKey(opcode);

    public static string GetAliasName(uint opcode)
        => AliasNames.TryGetValue(opcode, out var name) ? name : GetName(opcode);

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
        if (AliasNames.TryGetValue(opcode, out name))
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
