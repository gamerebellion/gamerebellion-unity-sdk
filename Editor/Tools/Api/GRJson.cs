using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// A small JSON reader for API responses whose shape isn't pinned down yet.
    /// JsonUtility needs a fixed class and can't read a top-level array, a numeric id
    /// into a string, or a field it doesn't know about; this reads anything into
    /// dictionaries, lists, strings, longs, doubles, bools and nulls.
    /// </summary>
    internal static class GRJson
    {
        public static object Parse(string json)
        {
            var reader = new Reader(json);
            var value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd)
            {
                throw new FormatException("Unexpected text after the JSON value.");
            }

            return value;
        }

        /// <summary>The first of <paramref name="keys"/> that holds a scalar, as text; null if none does.</summary>
        public static string Text(Dictionary<string, object> item, params string[] keys)
        {
            if (item == null) return null;
            foreach (var key in keys)
            {
                if (!item.TryGetValue(key, out var value) || value == null) continue;
                switch (value)
                {
                    case string text: if (text.Length > 0) return text; break;
                    case long number: return number.ToString(CultureInfo.InvariantCulture);
                    case double number: return number.ToString(CultureInfo.InvariantCulture);
                    case bool flag: return flag ? "true" : "false";
                }
            }

            return null;
        }

        /// <summary>True only for a JSON true (or "true"); anything else, including a missing key, is false.</summary>
        public static bool Bool(Dictionary<string, object> item, string key) =>
            item != null && item.TryGetValue(key, out var value) && (value is bool flag ? flag : value is string text && text == "true");

        public static Dictionary<string, object> GetObject(Dictionary<string, object> item, string key) =>
            item != null && item.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;

        public static List<object> GetArray(Dictionary<string, object> item, string key) =>
            item != null && item.TryGetValue(key, out var value) ? value as List<object> : null;

        /// <summary>A field's JSON type, for describing a response without its values.</summary>
        public static string TypeOf(object value)
        {
            switch (value)
            {
                case null: return "null";
                case string _: return "string";
                case long _: return "integer";
                case double _: return "number";
                case bool _: return "boolean";
                case List<object> _: return "array";
                default: return "object";
            }
        }

        /// <summary>
        /// What <see cref="Parse"/> reads, as indented JSON, keys in their order. For
        /// rewriting a config file another tool owns without losing anything in it.
        /// </summary>
        public static string Write(object value)
        {
            var text = new StringBuilder();
            Write(text, value, 0);
            return text.ToString();
        }

        private static void Write(StringBuilder text, object value, int depth)
        {
            switch (value)
            {
                case null:
                    text.Append("null");
                    break;
                case string s:
                    WriteString(text, s);
                    break;
                case bool b:
                    text.Append(b ? "true" : "false");
                    break;
                case long number:
                    text.Append(number.ToString(CultureInfo.InvariantCulture));
                    break;
                case double number:
                    text.Append(number.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case List<object> list:
                    if (list.Count == 0)
                    {
                        text.Append("[]");
                        break;
                    }

                    text.Append('[');
                    for (var i = 0; i < list.Count; i++)
                    {
                        text.Append(i == 0 ? "\n" : ",\n").Append(' ', (depth + 1) * 2);
                        Write(text, list[i], depth + 1);
                    }

                    text.Append('\n').Append(' ', depth * 2).Append(']');
                    break;
                case Dictionary<string, object> map:
                    if (map.Count == 0)
                    {
                        text.Append("{}");
                        break;
                    }

                    text.Append('{');
                    var first = true;
                    foreach (var pair in map)
                    {
                        text.Append(first ? "\n" : ",\n").Append(' ', (depth + 1) * 2);
                        WriteString(text, pair.Key);
                        text.Append(": ");
                        Write(text, pair.Value, depth + 1);
                        first = false;
                    }

                    text.Append('\n').Append(' ', depth * 2).Append('}');
                    break;
                default:
                    throw new ArgumentException($"Can't write a {value.GetType().Name} as JSON.");
            }
        }

        private static void WriteString(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    default:
                        if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else text.Append(c);
                        break;
                }
            }

            text.Append('"');
        }

        private sealed class Reader
        {
            private readonly string _json;
            private int _index;

            public Reader(string json)
            {
                _json = json ?? string.Empty;
            }

            public bool AtEnd => _index >= _json.Length;

            public object ReadValue()
            {
                SkipWhitespace();
                if (AtEnd) throw new FormatException("Unexpected end of JSON.");
                switch (_json[_index])
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            private Dictionary<string, object> ReadObject()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                _index++; // {
                SkipWhitespace();
                if (Peek('}')) { _index++; return result; }
                while (true)
                {
                    SkipWhitespace();
                    var key = ReadString();
                    SkipWhitespace();
                    Consume(':');
                    result[key] = ReadValue();
                    SkipWhitespace();
                    if (Peek(',')) { _index++; continue; }
                    Consume('}');
                    return result;
                }
            }

            private List<object> ReadArray()
            {
                var result = new List<object>();
                _index++; // [
                SkipWhitespace();
                if (Peek(']')) { _index++; return result; }
                while (true)
                {
                    result.Add(ReadValue());
                    SkipWhitespace();
                    if (Peek(',')) { _index++; continue; }
                    Consume(']');
                    return result;
                }
            }

            private string ReadString()
            {
                Consume('"');
                var text = new StringBuilder();
                while (!AtEnd)
                {
                    var c = _json[_index++];
                    if (c == '"') return text.ToString();
                    if (c != '\\') { text.Append(c); continue; }
                    if (AtEnd) break;
                    var escape = _json[_index++];
                    switch (escape)
                    {
                        case 'b': text.Append('\b'); break;
                        case 'f': text.Append('\f'); break;
                        case 'n': text.Append('\n'); break;
                        case 'r': text.Append('\r'); break;
                        case 't': text.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _json.Length) throw new FormatException("Bad \\u escape.");
                            text.Append((char)int.Parse(_json.Substring(_index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _index += 4;
                            break;
                        default: text.Append(escape); break; // \" \\ \/
                    }
                }

                throw new FormatException("Unterminated string.");
            }

            private object ReadNumber()
            {
                var start = _index;
                while (!AtEnd && "+-0123456789.eE".IndexOf(_json[_index]) >= 0) _index++;
                var token = _json.Substring(start, _index - start);
                if (token.Length == 0) throw new FormatException($"Unexpected character '{_json[start]}'.");
                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
                return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            public void SkipWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(_json[_index])) _index++;
            }

            private bool Peek(char c) => !AtEnd && _json[_index] == c;

            private void Consume(char c)
            {
                if (!Peek(c)) throw new FormatException($"Expected '{c}' at {_index}.");
                _index++;
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_json, _index, word, 0, word.Length) != 0) throw new FormatException($"Expected '{word}' at {_index}.");
                _index += word.Length;
            }
        }
    }
}
