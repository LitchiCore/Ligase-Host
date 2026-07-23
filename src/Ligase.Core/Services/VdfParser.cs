using System.Text;

namespace Ligase.Host.Core.Services;

internal sealed class VdfObject : Dictionary<string, object>
{
    public VdfObject() : base(StringComparer.OrdinalIgnoreCase) { }
    public string? GetString(string key) => TryGetValue(key, out var value) ? value as string : null;
    public VdfObject? GetObject(string key) => TryGetValue(key, out var value) ? value as VdfObject : null;
}

internal static class VdfParser
{
    public static VdfObject Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokenizer = new Tokenizer(text);
        var root = new VdfObject();
        ParseEntries(tokenizer, root, false);
        return root;
    }

    private static void ParseEntries(Tokenizer tokenizer, VdfObject target, bool stopAtClosingBrace)
    {
        while (tokenizer.Read() is { } key)
        {
            if (key == "}")
            {
                if (!stopAtClosingBrace)
                {
                    throw new FormatException("Unexpected closing brace in VDF document.");
                }
                return;
            }

            var value = tokenizer.Read() ?? throw new FormatException($"Missing value for VDF key '{key}'.");
            if (value == "{")
            {
                var child = new VdfObject();
                ParseEntries(tokenizer, child, true);
                target[key] = child;
            }
            else if (value == "}")
            {
                throw new FormatException($"Missing value for VDF key '{key}'.");
            }
            else
            {
                target[key] = value;
            }
        }

        if (stopAtClosingBrace)
        {
            throw new FormatException("Unclosed object in VDF document.");
        }
    }

    private sealed class Tokenizer(string text)
    {
        private int _index;

        public string? Read()
        {
            SkipWhitespaceAndComments();
            if (_index >= text.Length) return null;
            var current = text[_index];
            if (current is '{' or '}')
            {
                _index++;
                return current.ToString();
            }
            return current == '"' ? ReadQuoted() : ReadBare();
        }

        private string ReadQuoted()
        {
            _index++;
            var result = new StringBuilder();
            while (_index < text.Length)
            {
                var current = text[_index++];
                if (current == '"') return result.ToString();
                if (current == '\\' && _index < text.Length)
                {
                    var escaped = text[_index++];
                    result.Append(escaped switch
                    {
                        'n' => '\n', 'r' => '\r', 't' => '\t',
                        '\\' => '\\', '"' => '"', _ => escaped
                    });
                }
                else result.Append(current);
            }
            throw new FormatException("Unclosed quoted string in VDF document.");
        }

        private string ReadBare()
        {
            var start = _index;
            while (_index < text.Length && !char.IsWhiteSpace(text[_index]) && text[_index] is not '{' and not '}')
            {
                _index++;
            }
            return text[start.._index];
        }

        private void SkipWhitespaceAndComments()
        {
            while (_index < text.Length)
            {
                if (char.IsWhiteSpace(text[_index]))
                {
                    _index++;
                    continue;
                }
                if (_index + 1 < text.Length && text[_index] == '/' && text[_index + 1] == '/')
                {
                    _index += 2;
                    while (_index < text.Length && text[_index] != '\n') _index++;
                    continue;
                }
                break;
            }
        }
    }
}
