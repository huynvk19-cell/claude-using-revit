using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ClaudeUsingRevit.Setup
{
    /// <summary>
    /// A small JSON reader / writer (no library: the installer is one .exe). Objects keep their key order and numbers
    /// keep their exact text, so a file read and written back changes only what was edited.
    /// Object = List&lt;KeyValuePair&lt;string, object&gt;&gt; (JsonObject), array = List&lt;object&gt;, number = JsonNumber,
    /// string, bool, null.
    /// </summary>
    public class JsonObject : List<KeyValuePair<string, object>>
    {
        public object Get(string key)
        {
            foreach (var kv in this) if (kv.Key == key) return kv.Value;
            return null;
        }

        public void Set(string key, object value)
        {
            for (int i = 0; i < Count; i++)
                if (this[i].Key == key) { this[i] = new KeyValuePair<string, object>(key, value); return; }
            Add(new KeyValuePair<string, object>(key, value));
        }

        public bool Remove(string key)
        {
            for (int i = 0; i < Count; i++)
                if (this[i].Key == key) { RemoveAt(i); return true; }
            return false;
        }
    }

    public class JsonNumber
    {
        public readonly string Text;
        public JsonNumber(string text) { Text = text; }
        public override string ToString() => Text;
    }

    public static class MiniJson
    {
        public static object Parse(string text)
        {
            int i = 0;
            var v = Value(text, ref i);
            Skip(text, ref i);
            if (i != text.Length) throw new FormatException("JSON: extra text at " + i);
            return v;
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON: unexpected end");
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start) throw new FormatException("JSON: bad value at " + i);
            return new JsonNumber(s.Substring(start, i - start));
        }

        static JsonObject Obj(string s, ref int i)
        {
            var o = new JsonObject();
            i++;
            Skip(s, ref i);
            if (s[i] == '}') { i++; return o; }
            while (true)
            {
                Skip(s, ref i);
                string key = Str(s, ref i);
                Skip(s, ref i);
                if (s[i] != ':') throw new FormatException("JSON: ':' expected at " + i);
                i++;
                o.Add(new KeyValuePair<string, object>(key, Value(s, ref i)));
                Skip(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return o; }
                throw new FormatException("JSON: ',' or '}' expected at " + i);
            }
        }

        static List<object> Arr(string s, ref int i)
        {
            var a = new List<object>();
            i++;
            Skip(s, ref i);
            if (s[i] == ']') { i++; return a; }
            while (true)
            {
                a.Add(Value(s, ref i));
                Skip(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return a; }
                throw new FormatException("JSON: ',' or ']' expected at " + i);
            }
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("JSON: string expected at " + i);
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("JSON: unterminated string");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
        }

        public static string Write(object v)
        {
            var sb = new StringBuilder();
            Write(sb, v, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v, int depth)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case JsonNumber n: sb.Append(n.Text); return;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case string s: Quote(sb, s); return;
                case JsonObject o:
                    if (o.Count == 0) { sb.Append("{}"); return; }
                    sb.Append("{\n");
                    for (int k = 0; k < o.Count; k++)
                    {
                        Indent(sb, depth + 1);
                        Quote(sb, o[k].Key);
                        sb.Append(": ");
                        Write(sb, o[k].Value, depth + 1);
                        sb.Append(k < o.Count - 1 ? ",\n" : "\n");
                    }
                    Indent(sb, depth);
                    sb.Append('}');
                    return;
                case List<object> a:
                    if (a.Count == 0) { sb.Append("[]"); return; }
                    sb.Append("[\n");
                    for (int k = 0; k < a.Count; k++)
                    {
                        Indent(sb, depth + 1);
                        Write(sb, a[k], depth + 1);
                        sb.Append(k < a.Count - 1 ? ",\n" : "\n");
                    }
                    Indent(sb, depth);
                    sb.Append(']');
                    return;
                default: Quote(sb, v.ToString()); return;
            }
        }

        static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);

        static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
