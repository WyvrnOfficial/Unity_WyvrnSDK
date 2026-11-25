// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WyvrnSDK.Haptics.EditorTools
{
    /// <summary>
    /// A parsed JSON value.
    ///
    /// Deliberately a tiny hand-rolled tree rather than UnityEngine.JsonUtility or a
    /// Newtonsoft dependency:
    ///  - JsonUtility needs a concrete [Serializable] mirror of the whole schema and
    ///    cannot express a field that is sometimes a string and sometimes an array,
    ///    which is exactly what WYVRN.config's Interrupts_Commands is.
    ///  - a NuGet/UPM JSON package would become a hard dependency of a public SDK
    ///    package and risk colliding with whatever the host project already uses.
    ///
    /// This is editor-only: the runtime never parses JSON, it hands the verbatim .haps
    /// text to HAR. So nothing here is on a hot path.
    /// </summary>
    public sealed class WyvrnJsonValue
    {
        public enum ValueKind
        {
            Null,
            Bool,
            Number,
            String,
            Array,
            Object,
        }

        public ValueKind Kind { get; private set; }

        private bool boolValue;
        private double numberValue;
        private string stringValue;
        private List<WyvrnJsonValue> arrayValue;
        private Dictionary<string, WyvrnJsonValue> objectValue;

        public static WyvrnJsonValue NewNull()
        {
            return new WyvrnJsonValue { Kind = ValueKind.Null };
        }

        public static WyvrnJsonValue NewBool(bool value)
        {
            return new WyvrnJsonValue { Kind = ValueKind.Bool, boolValue = value };
        }

        public static WyvrnJsonValue NewNumber(double value)
        {
            return new WyvrnJsonValue { Kind = ValueKind.Number, numberValue = value };
        }

        public static WyvrnJsonValue NewString(string value)
        {
            return new WyvrnJsonValue { Kind = ValueKind.String, stringValue = value };
        }

        public static WyvrnJsonValue NewArray(List<WyvrnJsonValue> value)
        {
            return new WyvrnJsonValue { Kind = ValueKind.Array, arrayValue = value };
        }

        public static WyvrnJsonValue NewObject(Dictionary<string, WyvrnJsonValue> value)
        {
            return new WyvrnJsonValue { Kind = ValueKind.Object, objectValue = value };
        }

        public bool IsObject
        {
            get { return Kind == ValueKind.Object; }
        }

        public bool IsArray
        {
            get { return Kind == ValueKind.Array; }
        }

        /// <summary>Elements of an array value; an empty list for anything else.</summary>
        public List<WyvrnJsonValue> AsArray()
        {
            return arrayValue ?? new List<WyvrnJsonValue>();
        }

        /// <summary>The string of a string value, or null.</summary>
        public string AsString()
        {
            return Kind == ValueKind.String ? stringValue : null;
        }

        /// <summary>The value of a bool value, or false for anything else.</summary>
        public bool AsBool()
        {
            return Kind == ValueKind.Bool && boolValue;
        }

        /// <summary>Returns the named field of an object value, or null if absent.</summary>
        public WyvrnJsonValue GetField(string name)
        {
            WyvrnJsonValue value;
            if (objectValue != null && objectValue.TryGetValue(name, out value))
            {
                return value;
            }
            return null;
        }

        public bool TryGetString(string name, out string value)
        {
            WyvrnJsonValue field = GetField(name);
            if (field != null && field.Kind == ValueKind.String)
            {
                value = field.stringValue;
                return true;
            }
            value = null;
            return false;
        }

        public bool TryGetBool(string name, out bool value)
        {
            WyvrnJsonValue field = GetField(name);
            if (field != null && field.Kind == ValueKind.Bool)
            {
                value = field.boolValue;
                return true;
            }
            value = false;
            return false;
        }

        public bool TryGetNumber(string name, out double value)
        {
            WyvrnJsonValue field = GetField(name);
            if (field != null && field.Kind == ValueKind.Number)
            {
                value = field.numberValue;
                return true;
            }
            value = 0.0;
            return false;
        }

        public bool TryGetObject(string name, out WyvrnJsonValue value)
        {
            WyvrnJsonValue field = GetField(name);
            if (field != null && field.Kind == ValueKind.Object)
            {
                value = field;
                return true;
            }
            value = null;
            return false;
        }

        public bool TryGetArray(string name, out List<WyvrnJsonValue> value)
        {
            WyvrnJsonValue field = GetField(name);
            if (field != null && field.Kind == ValueKind.Array)
            {
                value = field.arrayValue;
                return true;
            }
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Minimal recursive-descent JSON reader for the WYVRN importer: WYVRN.config and
    /// .haps inspection. Rejects malformed input with a message rather than throwing,
    /// because a bad file is a user-facing import error, not a bug.
    /// </summary>
    public static class WyvrnJson
    {
        public static bool TryParse(string text, out WyvrnJsonValue root, out string error)
        {
            root = null;
            error = null;

            if (string.IsNullOrEmpty(text))
            {
                error = "the file is empty.";
                return false;
            }

            int index = 0;

            // Strip a UTF-8 byte-order mark: the WYVRN tooling writes one, and it is not
            // part of the JSON grammar.
            if (text[0] == '﻿')
            {
                index = 1;
            }

            try
            {
                SkipWhitespace(text, ref index);
                root = ParseValue(text, ref index);
                SkipWhitespace(text, ref index);
                if (index != text.Length)
                {
                    error = string.Format("unexpected trailing content at offset {0}.", index);
                    root = null;
                    return false;
                }
                return true;
            }
            catch (WyvrnJsonException exception)
            {
                error = exception.Message;
                root = null;
                return false;
            }
        }

        private sealed class WyvrnJsonException : System.Exception
        {
            public WyvrnJsonException(string message) : base(message)
            {
            }
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length)
            {
                char c = text[index];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    ++index;
                    continue;
                }
                break;
            }
        }

        private static WyvrnJsonValue ParseValue(string text, ref int index)
        {
            if (index >= text.Length)
            {
                throw new WyvrnJsonException("unexpected end of input.");
            }

            char c = text[index];
            switch (c)
            {
                case '{':
                    return ParseObject(text, ref index);
                case '[':
                    return ParseArray(text, ref index);
                case '"':
                    return WyvrnJsonValue.NewString(ParseString(text, ref index));
                case 't':
                    Expect(text, ref index, "true");
                    return WyvrnJsonValue.NewBool(true);
                case 'f':
                    Expect(text, ref index, "false");
                    return WyvrnJsonValue.NewBool(false);
                case 'n':
                    Expect(text, ref index, "null");
                    return WyvrnJsonValue.NewNull();
                default:
                    return WyvrnJsonValue.NewNumber(ParseNumber(text, ref index));
            }
        }

        private static WyvrnJsonValue ParseObject(string text, ref int index)
        {
            ++index; // consume '{'
            Dictionary<string, WyvrnJsonValue> fields = new Dictionary<string, WyvrnJsonValue>();

            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == '}')
            {
                ++index;
                return WyvrnJsonValue.NewObject(fields);
            }

            while (true)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != '"')
                {
                    throw new WyvrnJsonException(string.Format("expected an object key at offset {0}.", index));
                }
                string key = ParseString(text, ref index);

                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != ':')
                {
                    throw new WyvrnJsonException(string.Format("expected ':' after key '{0}' at offset {1}.", key, index));
                }
                ++index;

                SkipWhitespace(text, ref index);
                // A duplicate key keeps the last value, matching common JSON readers.
                fields[key] = ParseValue(text, ref index);

                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    throw new WyvrnJsonException("unexpected end of input inside an object.");
                }
                if (text[index] == ',')
                {
                    ++index;
                    continue;
                }
                if (text[index] == '}')
                {
                    ++index;
                    return WyvrnJsonValue.NewObject(fields);
                }
                throw new WyvrnJsonException(string.Format("expected ',' or '}}' at offset {0}.", index));
            }
        }

        private static WyvrnJsonValue ParseArray(string text, ref int index)
        {
            ++index; // consume '['
            List<WyvrnJsonValue> items = new List<WyvrnJsonValue>();

            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == ']')
            {
                ++index;
                return WyvrnJsonValue.NewArray(items);
            }

            while (true)
            {
                SkipWhitespace(text, ref index);
                items.Add(ParseValue(text, ref index));

                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    throw new WyvrnJsonException("unexpected end of input inside an array.");
                }
                if (text[index] == ',')
                {
                    ++index;
                    continue;
                }
                if (text[index] == ']')
                {
                    ++index;
                    return WyvrnJsonValue.NewArray(items);
                }
                throw new WyvrnJsonException(string.Format("expected ',' or ']' at offset {0}.", index));
            }
        }

        private static string ParseString(string text, ref int index)
        {
            ++index; // consume opening quote
            StringBuilder builder = new StringBuilder();

            while (index < text.Length)
            {
                char c = text[index++];
                if (c == '"')
                {
                    return builder.ToString();
                }
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= text.Length)
                {
                    break;
                }
                char escape = text[index++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (index + 4 > text.Length)
                        {
                            throw new WyvrnJsonException("truncated \\u escape in a string.");
                        }
                        int code = int.Parse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        builder.Append((char)code);
                        index += 4;
                        break;
                    default:
                        throw new WyvrnJsonException(string.Format("unknown escape '\\{0}' in a string.", escape));
                }
            }

            throw new WyvrnJsonException("unterminated string.");
        }

        private static double ParseNumber(string text, ref int index)
        {
            int start = index;
            if (index < text.Length && (text[index] == '-' || text[index] == '+'))
            {
                ++index;
            }
            while (index < text.Length)
            {
                char c = text[index];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+')
                {
                    ++index;
                    continue;
                }
                break;
            }

            string token = text.Substring(start, index - start);
            double value;
            // Invariant culture, always: a machine with a comma decimal separator would
            // otherwise misread "0.7" as 7 (or fail), silently changing every authored gain.
            if (token.Length == 0 ||
                !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                throw new WyvrnJsonException(string.Format("'{0}' at offset {1} is not a valid number.", token, start));
            }
            return value;
        }

        private static void Expect(string text, ref int index, string literal)
        {
            if (index + literal.Length > text.Length ||
                string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
            {
                throw new WyvrnJsonException(string.Format("expected '{0}' at offset {1}.", literal, index));
            }
            index += literal.Length;
        }
    }
}
