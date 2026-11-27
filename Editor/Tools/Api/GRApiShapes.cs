using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Describes what each API call returned, field names and JSON types only, never
    /// values, in Library/GameRebellion/api-shapes.json. A development aid while the
    /// response DTOs aren't pinned down: it shows which fields the window could use.
    /// Library/ is per project and never committed.
    /// </summary>
    internal static class GRApiShapes
    {
        private const int SampledItems = 20;

        [Serializable]
        [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
        private sealed class Shape
        {
            public string name;
            public string path;
            public string recordedUtc;
            public int status;
            public string error;
            public string envelope;
            public int itemCount;
            public List<string> fields = new List<string>();
        }

        [Serializable]
        [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
        private sealed class ShapeFile
        {
            public List<Shape> calls = new List<Shape>();
        }

        public static void Record(string name, string path, object firstPage, List<Dictionary<string, object>> items)
        {
            try
            {
                var shape = new Shape
                {
                    name = name,
                    path = path,
                    recordedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    status = 200,
                    envelope = firstPage is List<object> ? "array"
                        : firstPage is Dictionary<string, object> envelope ? "object: " + string.Join(", ", envelope.Select(pair => $"{pair.Key} ({GRJson.TypeOf(pair.Value)})"))
                        : GRJson.TypeOf(firstPage),
                    itemCount = items.Count,
                };

                var fields = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
                foreach (var item in items.Take(SampledItems))
                {
                    Describe(item, string.Empty, fields, depth: 0);
                }

                shape.fields = fields.Select(pair => $"{pair.Key}: {string.Join(" | ", pair.Value)}").ToList();
                Save(shape);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameRebellion] Couldn't record the shape of {path}: {e.Message}");
            }
        }

        /// <summary>A call that failed: its status and the API's own message (DRF's "detail"), never a token.</summary>
        public static void RecordFailure(string name, string path, int status, string error)
        {
            try
            {
                Save(new Shape
                {
                    name = name,
                    path = path,
                    recordedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    status = status,
                    error = error,
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameRebellion] Couldn't record the failure of {path}: {e.Message}");
            }
        }

        private static void Save(Shape shape)
        {
            var folder = Path.Combine(GRProjectInfo.ProjectPath, "Library", "GameRebellion");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "api-shapes.json");
            var all = File.Exists(file) ? JsonUtility.FromJson<ShapeFile>(File.ReadAllText(file)) ?? new ShapeFile() : new ShapeFile();
            all.calls.RemoveAll(existing => existing.name == shape.name);
            all.calls.Add(shape);
            File.WriteAllText(file, JsonUtility.ToJson(all, true));
        }

        /// <summary>
        /// What kind of text a value is, never the text: enough to tell an SDK key (the
        /// sample game's is 64 hex characters) from a label, a URL or a date.
        /// </summary>
        private static string Format(string text)
        {
            if (text.Length == 0) return "empty";
            if (System.Text.RegularExpressions.Regex.IsMatch(text, "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")) return "uuid";
            if (text.StartsWith("http://", StringComparison.Ordinal) || text.StartsWith("https://", StringComparison.Ordinal)) return "url";
            if (System.Text.RegularExpressions.Regex.IsMatch(text, "^\\d{4}-\\d{2}-\\d{2}T")) return "datetime";
            if (System.Text.RegularExpressions.Regex.IsMatch(text, "^[0-9]+$")) return $"digits, {text.Length}";
            if (System.Text.RegularExpressions.Regex.IsMatch(text, "^[0-9a-fA-F]+$")) return $"hex, {text.Length}";
            if (System.Text.RegularExpressions.Regex.IsMatch(text, "^[A-Za-z0-9_\\-]+$") && text.Length >= 16) return $"token-like, {text.Length}";
            return "text";
        }

        private static void Describe(Dictionary<string, object> item, string prefix, SortedDictionary<string, SortedSet<string>> fields, int depth)
        {
            foreach (var pair in item)
            {
                var key = prefix + pair.Key;
                var type = pair.Value is string text ? "string(" + Format(text) + ")" : GRJson.TypeOf(pair.Value);
                if (pair.Value is List<object> list)
                {
                    type = "array<" + string.Join(" | ", list.Select(GRJson.TypeOf).Distinct().DefaultIfEmpty("empty")) + ">";
                    foreach (var element in list.OfType<Dictionary<string, object>>().Take(SampledItems))
                    {
                        if (depth < 2) Describe(element, key + "[].", fields, depth + 1);
                    }
                }

                if (!fields.TryGetValue(key, out var types))
                {
                    fields[key] = types = new SortedSet<string>(StringComparer.Ordinal);
                }

                types.Add(type);
                if (pair.Value is Dictionary<string, object> nested && depth < 2)
                {
                    Describe(nested, key + ".", fields, depth + 1);
                }
            }
        }
    }
}
