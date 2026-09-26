using ShadowForge.Formats.BDSL;
using ShadowForge.Formats.RPJ;

namespace ShadowForge.Tests.BDSL;

/// <summary>
/// Every integer the compiler reads accepts decimal and 0x-prefixed hex in either case, with an
/// optional sign, and compiles to the same bytes however it was written. The decompiler still
/// writes one spelling per field.
/// </summary>
public sealed class IntegerSpellingTests
{
    private const string Template = """
        scene "t" {
            version = "0.26"
            area = town
        {S}
        }

        {T} "e" id={ID} {
        {E}
            when {W} {
        {B}
            }
        }

        waypoint {WID}{WT} at (0, 0, 0) {
        {P}
        }

        """;

    private static readonly string[] Unsigned = ["31", "0x1F", "0x1f", "0X1F", "+31", "0x0000001F"];
    private static readonly string[] Signed = ["-5", "0xFFFFFFFB", "0xfffffffb", "4294967291", "-0x5"];
    private static readonly string[] Chara = ["kluke", "2", "0x2", "0X02"];
    private static readonly string[] FloatBits = ["1.5", "0x3FC00000", "0x3fc00000"];
    private static readonly string[] RawOffset = ["0xA4", "0xa4", "164"];

    public static TheoryData<string, string, string[], string> Cases() => new()
    {
        { "S", "    flags = {0}", Unsigned, "0" },
        { "S", "    stage_id = {0}", Unsigned, "0" },
        { "S", "    area_metadata = {0}", Unsigned, "0" },
        { "S", "    scene_version = {0}", Unsigned, "0" },
        { "S", "    area_origin_w = {0}", Unsigned, "0" },
        { "S", "    area_config = {0} {0} {0} {0} {0}", Unsigned, "0" },
        { "S", "    area = type_{0}", Unsigned, "0" },
        { "T", "entry_{0}", ["9", "0x9", "0X09"], "0" },
        { "ID", "{0}", Unsigned, "0" },
        { "E", "    ref_id = {0}", Unsigned, "0" },
        { "E", "    @runtime_ref {0}", Unsigned, "0" },
        { "E", "    @target_refs {0} {0} {0}", Unsigned, "0" },
        { "E", "    facing = {0}", FloatBits, "0" },
        { "W", "chapter {0}..{0}", Unsigned, "0" },
        { "W", "all..{0}", Unsigned, "0" },
        { "W", "var[{0}] == {0}", Unsigned, "0" },
        { "W", "var[1] == {0}", Signed, "0" },
        { "W", "item_count({0}) >= {0}", Unsigned, "0" },
        { "W", "savebit({0})", Unsigned, "0" },
        { "W", "cond({0}, {0}, {0}, {0})", Unsigned, "0" },
        { "B", "        @block_type {0}", Unsigned, "0" },
        { "B", "        @encounter {0}", Unsigned, "0" },
        { "B", "        @behavior {0}", Signed, "0" },
        { "B", "        @auto_run {0}", Unsigned, "0" },
        { "B", "        @auto_set_var {0}", Unsigned, "0" },
        { "B", "        @auto_set_var_index {0}", Unsigned, "0" },
        { "B", "        @linked_entry {0}", Unsigned, "0" },
        { "B", "        @spawn_flags {0}", Unsigned, "0" },
        { "B", "        @type_data {0}", Unsigned, "0" },
        { "B", "        @spawn_mode {0}", Unsigned, "0" },
        { "B", "        @render visible, {0}", ["0x400", "0x0400", "1024"], "0" },
        { "B", "        @spawn_angle {0}", FloatBits, "0" },
        { "B", "        @raw_header {0} 0x7", RawOffset, "0" },
        { "B", "        @raw_header 0xA4 {0}", Unsigned, "0" },
        { "B", "        var[{0}] = {0}", Unsigned, "0" },
        { "B", "        var[1] = {0}", Signed, "0" },
        { "B", "        var[1] += var[{0}]", Unsigned, "0" },
        { "B", "        flag[{0}] = {0}", Unsigned, "0" },
        { "B", "        var[1] = flag[{0}]", Unsigned, "0" },
        { "B", "        var[1] = random({0}, {0})", Unsigned, "0" },
        { "B", "        var[1] = chara({0}, 3)", Chara, "0" },
        { "B", "        @{0}:\n        goto @{0}", Unsigned, "0" },
        { "B", "        if var[{0}] == {0} then goto @{0}\n        @{0}:", Unsigned, "0" },
        { "B", "        if var[1] != {0} then end", Signed, "0" },
        { "B", "        if gold >= {0} {\n            end\n        }", Unsigned, "0" },
        { "B", "        if var[1] == 2 {\n            end\n        }\n        @{0}:", Unsigned, "0" },
        { "B", "        if item_count({0}) < {0} then end", Unsigned, "0" },
        { "B", "        if in_party({0}) then end", Chara, "0" },
        { "B", "        if overflow(item {0}, +{0}) then goto @{0}\n        @{0}:", Unsigned, "0" },
        { "B", "        wait(type={0}, frames={0})", Unsigned, "0" },
        { "B", "        give_gold(mode={0}, amount={0})", Unsigned, "0" },
        { "B", "        give_gold(mode=1, amount={0})", Signed, "0" },
        { "B", "        set_ailment(chara={0}, ailment=1, active=1)", Chara, "0" },
        { "B", "        set_variable(dest=var[{0}], op=0, value_type=0, value={0})", Unsigned, "0" },
        { "B", "        label(id=@{0})", Unsigned, "0" },
        { "B", "        op_{0}()", ["5020", "0x139C", "0x139c"], "5023" },
        { "WID", "{0}", Unsigned, "0" },
        { "WT", " type={0}", ["3", "0x3", "0X03"], "0" },
        { "P", "    priority = {0}", Unsigned, "0" },
        { "P", "    targets = [{0}, {0}, {0}]", Unsigned, "0" },
        { "P", "    @condition_value {0}", Unsigned, "0" },
        { "P", "    @unk_field_20 {0}", Unsigned, "0" },
        { "P", "    condition variable 0x0 == {0}", Unsigned, "0" },
        { "P", "    condition unk_{0} 0x0 == 1", ["10", "0xA", "0xa"], "0" },
        { "P", "    condition variable 0x0 op_{0} 1", ["3", "0x3", "0X3"], "0" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EverySpelling_CompilesToTheSameBytes(string slot, string fragment, string[] spellings, string different)
    {
        byte[] expected = Compile(slot, fragment, spellings[0]);
        foreach (string spelling in spellings.Skip(1))
            Assert.True(expected.AsSpan().SequenceEqual(Compile(slot, fragment, spelling)),
                $"'{spelling}' compiled differently from '{spellings[0]}' in '{fragment}'");

        Assert.False(expected.AsSpan().SequenceEqual(Compile(slot, fragment, different)),
            $"'{fragment}' ignored its value");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Decompile_WritesOneSpellingThatCompilesBack(string slot, string fragment, string[] spellings, string different)
    {
        _ = different;
        byte[] bytes = Compile(slot, fragment, spellings[^1]);
        string canonical = TextScene.Write(BinaryScene.Read(bytes));

        Assert.Equal(bytes, BinaryScene.Write(TextScene.Read(canonical)));
        Assert.Equal(canonical, TextScene.Write(BinaryScene.Read(Compile(slot, fragment, spellings[0]))));
    }

    [Theory]
    [InlineData("0x")]
    [InlineData("0xG1")]
    [InlineData("0x100000000")]
    [InlineData("4294967296")]
    [InlineData("-2147483649")]
    [InlineData("1_000")]
    [InlineData("12abc")]
    public void MalformedOrOutOfRange_IsRejected(string spelling)
    {
        Assert.ThrowsAny<FormatException>(() => Compile("E", "    ref_id = {0}", spelling));
        Assert.ThrowsAny<FormatException>(() => Compile("B", "        var[1] = {0}", spelling));
    }

    private static byte[] Compile(string slot, string fragment, string spelling)
    {
        var slots = new Dictionary<string, string>
        {
            ["S"] = "", ["T"] = "entity", ["ID"] = "1", ["E"] = "", ["W"] = "", ["B"] = "",
            ["WID"] = "0", ["WT"] = "",
        };
        slots[slot] = fragment.Replace("{0}", spelling);
        string text = Template;
        foreach (var (key, value) in slots)
            text = text.Replace("{" + key + "}", value);
        return BinaryScene.Write(TextScene.Read(text));
    }
}
