using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PipeWizard.Services;

/// <summary>
/// Line-preserving editor for the small TOML subset the port's Config.toml uses.
/// It deliberately does not try to be a general TOML library: it reads the keys
/// PipeWizard cares about and rewrites only those lines, leaving comments, unknown
/// keys and formatting untouched (the same rule the runtime's own writer follows).
/// </summary>
public sealed class TomlConfig
{
    private readonly List<string> _lines;
    private readonly string _newline;

    public string Path { get; }

    private TomlConfig(string path, List<string> lines, string newline)
    {
        Path = path;
        _lines = lines;
        _newline = newline;
    }

    public static TomlConfig Load(string path)
    {
        var newline = Environment.NewLine;
        var lines = new List<string>();
        try
        {
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var split = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
                var last = split.Length;
                if (last > 0 && split[last - 1].Length == 0)
                {
                    last--;
                }

                for (var i = 0; i < last; i++)
                {
                    lines.Add(split[i]);
                }
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"Could not read {path}: {exception.Message}");
        }

        return new TomlConfig(path, lines, newline);
    }

    public static TomlConfig Create(string path)
    {
        var lines = new List<string>
        {
            "# PipeWizard created this file because the port has not written one yet.",
            "# The port fills in defaults for every key that is missing.",
            string.Empty,
            "[paths]",
        };
        return new TomlConfig(path, lines, Environment.NewLine);
    }

    public string? GetString(string section, string key)
    {
        var raw = GetRaw(section, key);
        if (raw is null)
        {
            return null;
        }

        return ParseStringValue(raw) ?? raw.Trim();
    }

    public bool? GetBool(string section, string key)
    {
        var raw = StripComment(GetRaw(section, key));
        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    public double? GetNumber(string section, string key)
    {
        var raw = StripComment(GetRaw(section, key));
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public IReadOnlyList<string> GetStringArray(string section, string key)
    {
        var raw = GetRaw(section, key);
        if (raw is null)
        {
            return Array.Empty<string>();
        }

        return ParseStringArray(raw);
    }

    public void SetString(string section, string key, string value) => SetRaw(section, key, Quote(value));

    public void SetBool(string section, string key, bool value) =>
        SetRaw(section, key, value ? "true" : "false");

    public void SetNumber(string section, string key, double value) =>
        SetRaw(section, key, value.ToString("0.###", CultureInfo.InvariantCulture));

    public void SetStringArray(string section, string key, IEnumerable<string> values)
    {
        var parts = new List<string>();
        foreach (var value in values)
        {
            parts.Add(Quote(value));
        }

        SetRaw(section, key, "[" + string.Join(", ", parts) + "]");
    }

    public void Remove(string section, string key)
    {
        var range = FindSectionRange(section);
        if (range is null)
        {
            return;
        }

        for (var i = range.Value.Start; i < range.Value.End; i++)
        {
            var uncommented = StripComment(_lines[i]);
            var equals = uncommented.IndexOf('=');
            if (equals > 0 && uncommented[..equals].Trim() == key)
            {
                _lines.RemoveAt(i);
                return;
            }
        }
    }

    public void Save()
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var text = _lines.Count == 0 ? string.Empty : string.Join(_newline, _lines) + _newline;
        File.WriteAllText(Path, text, new UTF8Encoding(false));
    }

    private void SetRaw(string section, string key, string rawValue)
    {
        var range = FindSectionRange(section);
        if (range is null)
        {
            if (_lines.Count > 0 && !string.IsNullOrWhiteSpace(_lines[^1]))
            {
                _lines.Add(string.Empty);
            }

            _lines.Add($"[{section}]");
            _lines.Add($"{key} = {rawValue}");
            return;
        }

        for (var i = range.Value.Start; i < range.Value.End; i++)
        {
            var uncommented = StripComment(_lines[i]);
            var equals = uncommented.IndexOf('=');
            if (equals <= 0 || uncommented[..equals].Trim() != key)
            {
                continue;
            }

            var indent = _lines[i][..(_lines[i].Length - _lines[i].TrimStart().Length)];
            _lines[i] = $"{indent}{key} = {rawValue}";
            return;
        }

        var insertAt = range.Value.End;
        while (insertAt > range.Value.Start + 1 && string.IsNullOrWhiteSpace(_lines[insertAt - 1]))
        {
            insertAt--;
        }

        _lines.Insert(insertAt, $"{key} = {rawValue}");
    }

    private string? GetRaw(string section, string key)
    {
        var range = FindSectionRange(section);
        if (range is null)
        {
            return null;
        }

        for (var i = range.Value.Start; i < range.Value.End; i++)
        {
            var uncommented = StripComment(_lines[i]);
            var equals = uncommented.IndexOf('=');
            if (equals <= 0 || uncommented[..equals].Trim() != key)
            {
                continue;
            }

            return uncommented[(equals + 1)..].Trim();
        }

        return null;
    }

    /// <summary>
    /// Returns the line indexes a key may live in: the section header is excluded,
    /// and for the root table (empty section) every line before the first header.
    /// </summary>
    private (int Start, int End)? FindSectionRange(string section)
    {
        if (section.Length == 0)
        {
            var end = _lines.Count;
            for (var i = 0; i < _lines.Count; i++)
            {
                if (TryReadSectionHeader(_lines[i], out _))
                {
                    end = i;
                    break;
                }
            }

            return (0, end);
        }

        var header = -1;
        for (var i = 0; i < _lines.Count; i++)
        {
            if (TryReadSectionHeader(_lines[i], out var name) &&
                name.Equals(section, StringComparison.Ordinal))
            {
                header = i;
                break;
            }
        }

        if (header < 0)
        {
            return null;
        }

        var sectionEnd = _lines.Count;
        for (var i = header + 1; i < _lines.Count; i++)
        {
            if (TryReadSectionHeader(_lines[i], out _))
            {
                sectionEnd = i;
                break;
            }
        }

        return (header + 1, sectionEnd);
    }

    private static bool TryReadSectionHeader(string line, out string name)
    {
        name = string.Empty;
        var trimmed = StripComment(line).Trim();
        if (trimmed.Length < 3 || trimmed[0] != '[' || trimmed[^1] != ']' || trimmed.StartsWith("[[", StringComparison.Ordinal))
        {
            return false;
        }

        name = trimmed[1..^1].Trim();
        return name.Length > 0;
    }

    private static string StripComment(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        var inSingle = false;
        var inDouble = false;
        var escaped = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inDouble && ch == '\\' && !escaped)
            {
                escaped = true;
                continue;
            }

            if (ch == '\'' && !inDouble)
            {
                inSingle = !inSingle;
            }
            else if (ch == '"' && !inSingle && !escaped)
            {
                inDouble = !inDouble;
            }
            else if (ch == '#' && !inSingle && !inDouble)
            {
                return line[..i];
            }

            escaped = false;
        }

        return line;
    }

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string? ParseStringValue(string raw)
    {
        var text = StripComment(raw).Trim();
        if (text.Length < 2)
        {
            return null;
        }

        if (text[0] == '\'')
        {
            var end = text.IndexOf('\'', 1);
            return end > 0 ? text[1..end] : null;
        }

        if (text[0] != '"')
        {
            return null;
        }

        var builder = new StringBuilder();
        var escaped = false;
        for (var i = 1; i < text.Length; i++)
        {
            var ch = text[i];
            if (escaped)
            {
                switch (ch)
                {
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case 'u' when i + 4 < text.Length:
                        if (int.TryParse(text.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        {
                            builder.Append((char)code);
                            i += 4;
                        }

                        break;
                    default:
                        builder.Append(ch);
                        break;
                }

                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
            }
            else if (ch == '"')
            {
                return builder.ToString();
            }
            else
            {
                builder.Append(ch);
            }
        }

        return null;
    }

    private static List<string> ParseStringArray(string raw)
    {
        var values = new List<string>();
        var text = StripComment(raw).Trim();
        if (text.Length < 2 || text[0] != '[')
        {
            return values;
        }

        var i = 1;
        while (i < text.Length)
        {
            while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == ','))
            {
                i++;
            }

            if (i >= text.Length || text[i] == ']')
            {
                break;
            }

            var quoteChar = text[i];
            if (quoteChar != '"' && quoteChar != '\'')
            {
                // Bare value: accept it up to the next comma or bracket.
                var start = i;
                while (i < text.Length && text[i] != ',' && text[i] != ']')
                {
                    i++;
                }

                var bare = text[start..i].Trim();
                if (bare.Length > 0)
                {
                    values.Add(bare);
                }

                continue;
            }

            i++;
            var builder = new StringBuilder();
            var escaped = false;
            while (i < text.Length)
            {
                var ch = text[i];
                if (quoteChar == '"' && escaped)
                {
                    switch (ch)
                    {
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case '"':
                            builder.Append('"');
                            break;
                        case '\\':
                            builder.Append('\\');
                            break;
                        case 'u' when i + 4 < text.Length:
                            if (int.TryParse(text.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            {
                                builder.Append((char)code);
                                i += 4;
                            }

                            break;
                        default:
                            builder.Append(ch);
                            break;
                    }

                    escaped = false;
                }
                else if (quoteChar == '"' && ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == quoteChar)
                {
                    i++;
                    break;
                }
                else
                {
                    builder.Append(ch);
                }

                i++;
            }

            values.Add(builder.ToString());
        }

        return values;
    }
}
