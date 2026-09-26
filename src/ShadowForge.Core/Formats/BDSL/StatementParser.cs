using System.Text.RegularExpressions;
using ShadowForge.Scene.Script;
using ShadowForge.Scene.Script.Lift;

namespace ShadowForge.Formats.BDSL;

/// <summary>
/// Parses when-block bodies into statement trees.
/// </summary>
public static partial class StatementParser
{
    private static readonly Dictionary<string, (uint Type, uint Sub)> ScalarConditions = new()
    {
        ["gold"] = (3, 0), ["playtime"] = (4, 0), ["realtime"] = (4, 1), ["medals"] = (5, 0), ["chapter"] = (6, 0),
    };

    [GeneratedRegex(@"@([+-]?(?:0[xX][0-9A-Fa-f]+|\d+))")]
    private static partial Regex LabelRef();

    private sealed class Cursor(IReadOnlyList<string> lines, int firstLine)
    {
        public int Index;
        public uint NextFreshLabel;
        public int Count => lines.Count;
        public string LineAt(int index) => lines[index];
        public string Raw => lines[Index];
        public bool AtEnd => Index >= lines.Count;
        public int LineNumber => firstLine + Index;
        public FormatException Error(string message) => SceneTextSyntax.LineError(LineNumber, message);
        public FormatException ErrorAt(int index, string message) => SceneTextSyntax.LineError(firstLine + index, message);
    }

    /// <summary>
    /// Parses the body lines of one when-block. A structured if takes the label that follows its
    /// closing brace. Otherwise it gets a new label numbered above every @N the block mentions.
    /// </summary>
    public static List<Statement> ParseBlock(IReadOnlyList<string> lines, int firstLineNumber)
    {
        var cursor = new Cursor(lines, firstLineNumber);
        uint max = 0;
        bool any = false;
        foreach (var line in lines)
            foreach (Match m in LabelRef().Matches(line))
            {
                if (!TextHelper.TryParseInteger(m.Groups[1].ValueSpan, out uint id)) continue;
                any = true;
                max = Math.Max(max, id);
            }
        cursor.NextFreshLabel = any ? max + 1 : 0;
        return ParseBody(cursor, topLevel: true);
    }

    private static List<Statement> ParseBody(Cursor c, bool topLevel)
    {
        var list = new List<Statement>();
        while (!c.AtEnd)
        {
            string raw = c.Raw.TrimEnd('\r');
            string trimmed = raw.Trim();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                string text = raw.TrimStart();
                list.Add(new CommentStmt(text.Length > 2 && text[2] == ' ' ? text[3..] : text[2..]));
                c.Index++;
                continue;
            }
            string line = TextHelper.StripComment(raw).Trim();
            if (line.Length == 0) { c.Index++; continue; }
            if (line == "}")
            {
                if (topLevel) throw c.Error("unexpected '}'");
                c.Index++;
                return list;
            }
            bool disabled = line.StartsWith('!');
            string body = disabled ? line[1..].TrimStart() : line;

            if (body.EndsWith('{'))
            {
                if (disabled) throw c.Error("a block statement cannot be disabled");
                string head = body[..^1].TrimEnd();
                int headLine = c.Index;
                c.Index++;
                var inner = ParseBody(c, topLevel: false);
                list.Add(BlockStatement(c, headLine, head, inner, out uint? synthesizedLabel));
                if (synthesizedLabel is uint fresh) list.Add(new LabelStmt(fresh, false));
                continue;
            }
            list.Add(LineStatement(c, body, disabled));
            c.Index++;
        }
        if (!topLevel) throw c.Error("missing '}'");
        return list;
    }

    private static Statement BlockStatement(Cursor c, int headLine, string head, List<Statement> inner, out uint? synthesizedLabel)
    {
        synthesizedLabel = null;
        if (head == "init") return new InitStmt(inner);
        if (!head.StartsWith("if ", StringComparison.Ordinal)) throw c.ErrorAt(headLine, $"unknown block '{head}'");
        string cond = head[3..].Trim();
        uint label;
        if (PeekFollowingLabel(c, out uint existing)) label = existing;
        else { label = c.NextFreshLabel++; synthesizedLabel = label; }
        int resumeIndex = c.Index;
        c.Index = headLine;
        try
        {
            if (cond.StartsWith("!overflow(", StringComparison.Ordinal))
            {
                var args = ParseOverflow(c, cond[1..]);
                args[3] = label;
                return new OverflowIfStmt(args, false, inner);
            }
            var ifArgs = ParseCondition(c, cond);
            ifArgs[5] = 0; ifArgs[6] = 0; ifArgs[7] = 2; ifArgs[8] = label;
            return new IfStmt(ifArgs, false, inner);
        }
        finally
        {
            c.Index = resumeIndex;
        }
    }

    private static bool PeekFollowingLabel(Cursor c, out uint id)
    {
        id = 0;
        for (int look = c.Index; look < c.Count; look++)
        {
            string source = c.LineAt(look).TrimEnd('\r').Trim();
            if (source.StartsWith("//", StringComparison.Ordinal)) continue;
            string line = TextHelper.StripComment(source).Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('@') && line.EndsWith(':'))
            {
                try { id = TextHelper.ParseInteger(line[1..^1]); }
                catch (FormatException ex)
                {
                    throw c.ErrorAt(look, ex.Message);
                }
                return true;
            }
            return false;
        }
        return false;
    }

    private static Statement LineStatement(Cursor c, string body, bool disabled)
    {
        if (body.StartsWith('@') && body.EndsWith(':'))
            return new LabelStmt(ParseUInt(c, body[1..^1]), disabled);
        if (body.StartsWith("goto @", StringComparison.Ordinal))
            return new GotoStmt(ParseUInt(c, body[6..]), disabled);
        if (body == "end")
            return new EndStmt(disabled);
        if (body.StartsWith("raw ", StringComparison.Ordinal))
        {
            if (disabled) throw c.Error("raw data cannot be disabled");
            try { return new RawStmt(Convert.FromHexString(body[4..].Trim())); }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                throw c.Error(ex.Message);
            }
        }
        if (body.StartsWith("if ", StringComparison.Ordinal))
            return FlatIf(c, body[3..].Trim(), disabled);
        if (body.StartsWith("flag[", StringComparison.Ordinal))
        {
            var m = Regex.Match(body, @"^flag\[([^\]]+)\]\s*=\s*(\S+)$");
            if (!m.Success) throw c.Error($"bad flag assignment '{body}'");
            return new FlagStmt([ParseUInt(c, m.Groups[1].Value), 0, ParseUInt(c, m.Groups[2].Value), 0, 0, 0], disabled);
        }
        if (body.StartsWith("var[", StringComparison.Ordinal))
            return Assignment(c, body, disabled);
        if (body.StartsWith("battle(", StringComparison.Ordinal))
            return Battle(c, body, disabled);
        return Call(c, body, disabled);
    }

    private static Statement Assignment(Cursor c, string body, bool disabled)
    {
        var m = Regex.Match(body, @"^var\[([^\]]+)\]\s*(=|\+=|-=|\*=|/=)\s*(.+)$");
        if (!m.Success) throw c.Error($"bad assignment '{body}'");
        uint dest = ParseUInt(c, m.Groups[1].Value);
        uint op = (uint)Array.IndexOf(StatementSyntax.AssignOps, m.Groups[2].Value);
        string value = m.Groups[3].Value.Trim();
        var flag = Regex.Match(value, @"^flag\[([^\]]+)\]$");
        if (flag.Success)
        {
            if (op != 0) throw c.Error("flag reads only support '='");
            return new FlagStmt([ParseUInt(c, flag.Groups[1].Value), 1, dest, 0, 0, 0], disabled);
        }
        uint type, operand;
        if (StatementSyntax.ValueSources.TryGetValue(value, out uint keyword)) { type = keyword; operand = 0; }
        else if (value.StartsWith("var[", StringComparison.Ordinal)) { type = 1; operand = ParseValue(c, value, ValueFormat.PlainInt); }
        else if (value.StartsWith("random(", StringComparison.Ordinal))
        {
            var parts = Inner(c, value, "random").Split(',');
            if (parts.Length != 2) throw c.Error("random(min, max) expected");
            type = 2; operand = (ParseUInt(c, parts[0]) << 16) | (ParseUInt(c, parts[1]) & 0xFFFF);
        }
        else if (value.StartsWith("chara(", StringComparison.Ordinal))
        {
            var parts = Inner(c, value, "chara").Split(',');
            if (parts.Length != 2) throw c.Error("chara(member, property) expected");
            type = 3; operand = (ParseValue(c, parts[0], ValueFormat.Character) << 16) | (ParseUInt(c, parts[1]) & 0xFFFF);
        }
        else { type = 0; operand = ParseValue(c, value, ValueFormat.PlainInt); }
        return new AssignStmt([dest, op, type, operand, 0, 0], disabled);
    }

    private static Statement FlatIf(Cursor c, string rest, bool disabled)
    {
        int thenAt = IndexOfWord(rest, " then ");
        int elseAt = IndexOfWord(rest, " else ");
        int condEnd = rest.Length;
        if (thenAt >= 0) condEnd = Math.Min(condEnd, thenAt);
        if (elseAt >= 0) condEnd = Math.Min(condEnd, elseAt);
        string cond = rest[..condEnd].Trim();
        (uint tAct, uint tLbl) = (0, 0);
        (uint fAct, uint fLbl) = (0, 0);
        if (thenAt >= 0)
        {
            int end = elseAt > thenAt ? elseAt : rest.Length;
            (tAct, tLbl) = ParseAction(c, rest[(thenAt + 6)..end].Trim());
        }
        if (elseAt >= 0)
            (fAct, fLbl) = ParseAction(c, rest[(elseAt + 6)..].Trim());

        if (cond.StartsWith("overflow(", StringComparison.Ordinal))
        {
            if (tAct != 2 || fAct != 0) throw c.Error("overflow checks only support 'then goto @N'");
            var args = ParseOverflow(c, cond);
            args[3] = tLbl;
            return new OverflowIfStmt(args, disabled, null);
        }
        var ifArgs = ParseCondition(c, cond);
        ifArgs[5] = tAct; ifArgs[6] = tLbl; ifArgs[7] = fAct; ifArgs[8] = fLbl;
        return new IfStmt(ifArgs, disabled, null);
    }

    private static int IndexOfWord(string s, string word) => s.IndexOf(word, StringComparison.Ordinal);

    private static (uint Action, uint Label) ParseAction(Cursor c, string text)
    {
        if (text == "end") return (1, 0);
        if (text.StartsWith("goto @", StringComparison.Ordinal)) return (2, ParseUInt(c, text[6..]));
        throw c.Error($"expected 'end' or 'goto @N', got '{text}'");
    }

    private static uint[] ParseCondition(Cursor c, string cond)
    {
        var args = new uint[10];
        bool negated = cond.StartsWith('!');
        if (negated) cond = cond[1..].Trim();
        if (cond.StartsWith("in_party(", StringComparison.Ordinal))
        {
            args[0] = 2;
            args[2] = ParseValue(c, Inner(c, cond, "in_party"), ValueFormat.Character);
            args[4] = negated ? 0u : 1u;
            return args;
        }
        if (negated) throw c.Error("'!' is only valid before in_party() and overflow()");
        var count = Regex.Match(cond, @"^(item_count|inventory_count)\((\S+)\)\s*(<|>=)\s*(\S+)$");
        if (count.Success)
        {
            args[0] = 1;
            args[1] = count.Groups[1].Value == "inventory_count" ? 1u : 0u;
            args[2] = ParseUInt(c, count.Groups[2].Value);
            args[3] = ParseUInt(c, count.Groups[4].Value);
            args[4] = count.Groups[3].Value == "<" ? 0u : 1u;
            return args;
        }
        var cmp = Regex.Match(cond, @"^(\S+)\s*(==|>=|<=|!=|>|<)\s*(\S+)$");
        if (!cmp.Success) throw c.Error($"cannot parse condition '{cond}'");
        string lhs = cmp.Groups[1].Value;
        if (!CompareOp.TryGetIndex(cmp.Groups[2].Value, out args[4])) throw c.Error($"unknown compare operator '{cmp.Groups[2].Value}'");
        string rhs = cmp.Groups[3].Value;
        if (lhs.StartsWith("var[", StringComparison.Ordinal))
        {
            args[0] = 0;
            args[2] = ParseValue(c, lhs, ValueFormat.PlainInt);
            if (rhs.StartsWith("var[", StringComparison.Ordinal)) { args[1] = 1; args[3] = ParseValue(c, rhs, ValueFormat.PlainInt); }
            else args[3] = ParseValue(c, rhs, ValueFormat.PlainInt);
            return args;
        }
        if (!ScalarConditions.TryGetValue(lhs, out var scalar)) throw c.Error($"unknown condition subject '{lhs}'");
        args[0] = scalar.Type;
        args[1] = scalar.Sub;
        args[3] = ParseValue(c, rhs, ValueFormat.PlainInt);
        return args;
    }

    private static uint[] ParseOverflow(Cursor c, string cond)
    {
        var parts = Inner(c, cond, "overflow").Split(',');
        if (parts.Length != 2) throw c.Error("overflow(subject, +amount) expected");
        var args = new uint[6];
        string subject = parts[0].Trim();
        string amount = parts[1].Trim();
        if (!amount.StartsWith('+')) throw c.Error("overflow amount must start with '+'");
        amount = amount[1..].Trim();
        if (subject.StartsWith("item ", StringComparison.Ordinal))
        {
            string item = subject[5..].Trim();
            args[0] = 0;
            args[1] = ParseValue(c, item, ValueFormat.PlainInt);
            if (item.StartsWith("var[", StringComparison.Ordinal)) args[4] |= 2;
        }
        else if (subject == "gold") args[0] = 1;
        else if (subject == "medals") args[0] = 2;
        else throw c.Error($"unknown overflow subject '{subject}'");
        args[2] = ParseValue(c, amount, ValueFormat.PlainInt);
        if (amount.StartsWith("var[", StringComparison.Ordinal)) args[4] |= 4;
        return args;
    }

    private static Statement Battle(Cursor c, string body, bool disabled)
    {
        int close = body.IndexOf(')');
        if (close < 0) throw c.Error("missing ')' in battle");
        var args = ParseArgs(c, OpcodeTable.Get(OpcodeTable.Battle), body[7..close]);
        string rest = body[(close + 1)..].Trim();
        int win = IndexOfWord(" " + rest, " on_win ");
        int lose = IndexOfWord(" " + rest, " on_lose ");
        if (win >= 0)
        {
            int end = lose > win ? lose : rest.Length + 1;
            (args[5], args[6]) = ParseAction(c, (" " + rest)[(win + 8)..end].Trim());
        }
        if (lose >= 0)
            (args[7], args[8]) = ParseAction(c, (" " + rest)[(lose + 9)..].Trim());
        if (win < 0 && lose < 0 && rest.Length > 0) throw c.Error($"unexpected '{rest}' after battle(...)");
        return new BattleStmt(args, disabled);
    }

    private static Statement Call(Cursor c, string body, bool disabled)
    {
        int open = body.IndexOf('(');
        if (open <= 0 || !body.EndsWith(')')) throw c.Error($"cannot parse statement '{body}'");
        string name = body[..open].Trim();
        if (name.StartsWith("op_", StringComparison.Ordinal) && TextHelper.TryParseInteger(name.AsSpan(3), out uint number))
            name = $"op_{number}";
        if (!OpcodeTable.TryGetByName(name, out var spec)) throw c.Error($"unknown opcode '{name}'");
        var args = ParseArgs(c, spec, body[(open + 1)..^1]);
        return new CallStmt(spec.Opcode, args, disabled);
    }

    private static uint[] ParseArgs(Cursor c, OpcodeSpec spec, string inner)
    {
        inner = inner.Trim();
        if (inner.Length == 0) return new uint[spec.Words];
        var parts = inner.Split(',');
        var indices = new int[parts.Length];
        int words = spec.Words;
        for (int i = 0; i < parts.Length; i++)
        {
            int eq = parts[i].IndexOf('=');
            if (eq < 0) throw c.Error($"argument '{parts[i].Trim()}' must be name=value");
            string name = parts[i][..eq].Trim();
            int index = spec.IndexOfArg(name);
            if (index < 0) throw c.Error($"{spec.Name} has no argument '{name}'");
            if (Array.IndexOf(indices, index, 0, i) >= 0)
                throw c.Error($"{spec.Name} argument '{name}' repeats index {index}");
            indices[i] = index;
            words = Math.Max(words, index + 1);
        }

        var args = new uint[words];
        for (int i = 0; i < parts.Length; i++)
        {
            int eq = parts[i].IndexOf('=');
            args[indices[i]] = ParseValue(c, parts[i][(eq + 1)..], spec.ArgAt(indices[i]));
        }
        return args;
    }

    private static string Inner(Cursor c, string text, string head)
    {
        if (!text.StartsWith(head + "(", StringComparison.Ordinal) || !text.EndsWith(')'))
            throw c.Error($"expected {head}(...)");
        return text[(head.Length + 1)..^1];
    }

    private static uint ParseValue(Cursor c, string token, ArgSpec spec)
    {
        try { return ValueFormat.Parse(token, spec); }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw c.Error(ex.Message);
        }
    }

    private static uint ParseUInt(Cursor c, string token) => ParseValue(c, token, ValueFormat.PlainInt);
}
