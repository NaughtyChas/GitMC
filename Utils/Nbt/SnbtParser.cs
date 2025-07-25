using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using fNbt;
using GitMC.Utils;

namespace GitMC.Utils.Nbt
{
    public class SnbtParser
    {
        private static readonly Regex DOUBLE_PATTERN_NOSUFFIX = new("^([-+]?(?:[0-9]+[.]|[0-9]*[.][0-9]+)(?:e[-+]?[0-9]+)?)$", RegexOptions.IgnoreCase);
        private static readonly Regex DOUBLE_PATTERN = new("^([-+]?(?:[0-9]+[.]?|[0-9]*[.][0-9]+)(?:e[-+]?[0-9]+)?d)$", RegexOptions.IgnoreCase);
        private static readonly Regex FLOAT_PATTERN = new("^([-+]?(?:[0-9]+[.]?|[0-9]*[.][0-9]+)(?:e[-+]?[0-9]+)?f)$", RegexOptions.IgnoreCase);
        private static readonly Regex BYTE_PATTERN = new("^([-+]?(?:0|[1-9][0-9]*)b)$", RegexOptions.IgnoreCase);
        private static readonly Regex LONG_PATTERN = new("^([-+]?(?:0|[1-9][0-9]*)l)$", RegexOptions.IgnoreCase);
        private static readonly Regex SHORT_PATTERN = new("^([-+]?(?:0|[1-9][0-9]*)s)$", RegexOptions.IgnoreCase);
        private static readonly Regex INT_PATTERN = new("^([-+]?(?:0|[1-9][0-9]*))$");

        private readonly StringReader Reader;

        public static Failable<NbtTag> TryParse(string snbt, bool named)
        {
            return new Failable<NbtTag>(() => Parse(snbt, named), "Parse SNBT");
        }

        public static NbtTag Parse(string snbt, bool named)
        {
            var parser = new SnbtParser(snbt);
            var value = named ? parser.ReadNamedValue() : parser.ReadValue();
            parser.Finish();
            return value;
        }

        public static bool ClassicTryParse(string snbt, bool named, out NbtTag? tag)
        {
            try
            {
                tag = Parse(snbt, named);
                return true;
            }
            catch (Exception)
            {
                tag = null;
                return false;
            }
        }

        private SnbtParser(string snbt)
        {
            snbt = snbt.TrimStart();
            Reader = new StringReader(snbt);
        }

        private NbtTag ReadValue()
        {
            Reader.SkipWhitespace();
            char next = Reader.Peek();
            if (next == Snbt.COMPOUND_OPEN)
                return ReadCompound();
            if (next == Snbt.LIST_OPEN)
                return ReadListLike();
            return ReadTypedValue();
        }

        private NbtTag ReadNamedValue()
        {
            string key = ReadKey();
            Expect(Snbt.NAME_VALUE_SEPARATOR);
            NbtTag value = ReadValue();
            value.Name = key;
            return value;
        }

        private void Finish()
        {
            Reader.SkipWhitespace();
            if (Reader.CanRead())
                throw new FormatException($"Trailing data found after position {Reader.Cursor}");
        }

        private NbtCompound ReadCompound()
        {
            Expect(Snbt.COMPOUND_OPEN);
            Reader.SkipWhitespace();
            var compound = new NbtCompound();
            while (Reader.CanRead() && Reader.Peek() != Snbt.COMPOUND_CLOSE)
            {
                var value = ReadNamedValue();
                compound.Add(value);
                if (!ReadSeparator())
                    break;
            }
            Expect(Snbt.COMPOUND_CLOSE);
            return compound;
        }

        private bool ReadSeparator()
        {
            Reader.SkipWhitespace();
            if (Reader.CanRead() && Reader.Peek() == Snbt.VALUE_SEPARATOR)
            {
                Reader.Read();
                Reader.SkipWhitespace();
                return true;
            }
            return false;
        }

        private string ReadKey()
        {
            Reader.SkipWhitespace();
            if (!Reader.CanRead())
                throw new FormatException($"Expected a key, but reached end of data");
            return Reader.ReadString();
        }

        private NbtTag ReadListLike()
        {
            if (Reader.CanRead(3) && !StringReader.IsQuote(Reader.Peek(1)) && Reader.Peek(2) == Snbt.ARRAY_DELIMITER)
                return ReadArray();
            return ReadList();
        }

        private NbtTag ReadArray()
        {
            Expect(Snbt.LIST_OPEN);
            char type = Reader.Read();
            Reader.Read(); // skip semicolon
            Reader.SkipWhitespace();
            if (!Reader.CanRead())
                throw new FormatException($"Expected array to end, but reached end of data");
            if (type == Snbt.BYTE_ARRAY_PREFIX)
                return ReadArray(NbtTagType.Byte);
            if (type == Snbt.LONG_ARRAY_PREFIX)
                return ReadArray(NbtTagType.Long);
            if (type == Snbt.INT_ARRAY_PREFIX)
                return ReadArray(NbtTagType.Int);
            throw new FormatException($"'{type}' is not a valid array type ({Snbt.BYTE_ARRAY_PREFIX}, {Snbt.LONG_ARRAY_PREFIX}, or {Snbt.INT_ARRAY_PREFIX})");
        }

        private NbtTag ReadArray(NbtTagType arraytype)
        {
            // Optimized: Use specialized arrays and direct value parsing to avoid creating temporary NBT objects
            if (arraytype == NbtTagType.Byte)
            {
                var list = new List<byte>();
                while (Reader.Peek() != Snbt.LIST_CLOSE)
                {
                    var value = ReadDirectByteValue();
                    list.Add(value);
                    if (!ReadSeparator())
                        break;
                }
                Expect(Snbt.LIST_CLOSE);
                return new NbtByteArray(list.ToArray());
            }
            else if (arraytype == NbtTagType.Long)
            {
                var list = new List<long>();
                while (Reader.Peek() != Snbt.LIST_CLOSE)
                {
                    var value = ReadDirectLongValue();
                    list.Add(value);
                    if (!ReadSeparator())
                        break;
                }
                Expect(Snbt.LIST_CLOSE);
                return new NbtLongArray(list.ToArray());
            }
            else // Int array
            {
                var list = new List<int>();
                while (Reader.Peek() != Snbt.LIST_CLOSE)
                {
                    var value = ReadDirectIntValue();
                    list.Add(value);
                    if (!ReadSeparator())
                        break;
                }
                Expect(Snbt.LIST_CLOSE);
                return new NbtIntArray(list.ToArray());
            }
        }

        private NbtList ReadList()
        {
            Expect(Snbt.LIST_OPEN);
            Reader.SkipWhitespace();
            if (!Reader.CanRead())
                throw new FormatException($"Expected list to end, but reached end of data");
            var list = new NbtList();
            while (Reader.Peek() != Snbt.LIST_CLOSE)
            {
                var tag = ReadValue();
                list.Add(tag);
                if (!ReadSeparator())
                    break;
            }
            Expect(Snbt.LIST_CLOSE);
            return list;
        }

        private NbtTag ReadTypedValue()
        {
            Reader.SkipWhitespace();
            if (StringReader.IsQuote(Reader.Peek()))
                return new NbtString(Reader.ReadQuotedString());
            string str = Reader.ReadUnquotedString();
            if (str == "")
                throw new FormatException($"Expected typed value to be non-empty");
            return TypeTag(str);
        }

        private NbtTag TypeTag(string str)
        {
            try
            {
                string sub = str[0..^1];
                if (FLOAT_PATTERN.IsMatch(str))
                    return new NbtFloat(float.Parse(sub, NumberStyles.Float, CultureInfo.InvariantCulture));
                if (BYTE_PATTERN.IsMatch(str))
                    return new NbtByte((byte)sbyte.Parse(sub));
                if (LONG_PATTERN.IsMatch(str))
                    return new NbtLong(long.Parse(sub));
                if (SHORT_PATTERN.IsMatch(str))
                    return new NbtShort(short.Parse(sub));
                if (INT_PATTERN.IsMatch(str))
                    return new NbtInt(int.Parse(str));
                if (DOUBLE_PATTERN.IsMatch(str))
                    return new NbtDouble(double.Parse(sub, NumberStyles.Float, CultureInfo.InvariantCulture));
                if (DOUBLE_PATTERN_NOSUFFIX.IsMatch(str))
                    return new NbtDouble(double.Parse(str, NumberStyles.Float, CultureInfo.InvariantCulture));
                var special = SpecialCase(str);
                if (special != null)
                    return special;
            }
            catch (FormatException)
            { }
            catch (OverflowException)
            { }
            return new NbtString(str);
        }

        private NbtTag? SpecialCase(string text)
        {
            if (String.IsNullOrEmpty(text))
                return null;
            if (text[^1] == Snbt.FLOAT_SUFFIX)
            {
                var special_float = DataUtils.TryParseSpecialFloat(text[0..^1]);
                if (special_float != null)
                    return new NbtFloat(special_float.Value);
            }
            if (text[^1] == Snbt.DOUBLE_SUFFIX)
            {
                var special_double = DataUtils.TryParseSpecialFloat(text[0..^1]);
                if (special_double != null)
                    return new NbtDouble(special_double.Value);
            }
            var special_double2 = DataUtils.TryParseSpecialDouble(text);
            if (special_double2 != null)
                return new NbtDouble(special_double2.Value);
            var special_byte = TryParseSpecialByte(text);
            if (special_byte != null)
                return new NbtByte((byte)special_byte);
            return null;
        }

        private static sbyte? TryParseSpecialByte(string value)
        {
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
                return 0;
            return null;
        }

        // Optimized: Direct value parsing methods to avoid creating temporary NBT objects for arrays
        private byte ReadDirectByteValue()
        {
            Reader.SkipWhitespace();
            if (StringReader.IsQuote(Reader.Peek()))
                throw new FormatException("Array values cannot be quoted strings");
            
            string str = Reader.ReadUnquotedString();
            if (str == "")
                throw new FormatException("Expected byte value to be non-empty");

            try
            {
                // Handle special boolean cases
                if (str.Equals("true", StringComparison.OrdinalIgnoreCase))
                    return 1;
                if (str.Equals("false", StringComparison.OrdinalIgnoreCase))
                    return 0;

                // Handle byte suffix
                if (BYTE_PATTERN.IsMatch(str))
                {
                    string sub = str[0..^1];
                    return (byte)sbyte.Parse(sub);
                }
                
                // Handle plain integer that fits in byte range
                if (INT_PATTERN.IsMatch(str))
                {
                    var intVal = int.Parse(str);
                    if (intVal >= sbyte.MinValue && intVal <= sbyte.MaxValue)
                        return (byte)(sbyte)intVal;
                    throw new OverflowException($"Value {intVal} is out of range for byte");
                }
                
                throw new FormatException($"'{str}' is not a valid byte value");
            }
            catch (FormatException)
            {
                throw;
            }
            catch (OverflowException)
            {
                throw;
            }
        }

        private long ReadDirectLongValue()
        {
            Reader.SkipWhitespace();
            if (StringReader.IsQuote(Reader.Peek()))
                throw new FormatException("Array values cannot be quoted strings");
            
            string str = Reader.ReadUnquotedString();
            if (str == "")
                throw new FormatException("Expected long value to be non-empty");

            try
            {
                // Handle long suffix
                if (LONG_PATTERN.IsMatch(str))
                {
                    string sub = str[0..^1];
                    return long.Parse(sub);
                }
                
                // Handle plain integer
                if (INT_PATTERN.IsMatch(str))
                {
                    return long.Parse(str);
                }
                
                throw new FormatException($"'{str}' is not a valid long value");
            }
            catch (FormatException)
            {
                throw;
            }
            catch (OverflowException)
            {
                throw;
            }
        }

        private int ReadDirectIntValue()
        {
            Reader.SkipWhitespace();
            if (StringReader.IsQuote(Reader.Peek()))
                throw new FormatException("Array values cannot be quoted strings");
            
            string str = Reader.ReadUnquotedString();
            if (str == "")
                throw new FormatException("Expected int value to be non-empty");

            try
            {
                // Handle plain integer
                if (INT_PATTERN.IsMatch(str))
                {
                    return int.Parse(str);
                }
                
                throw new FormatException($"'{str}' is not a valid int value");
            }
            catch (FormatException)
            {
                throw;
            }
            catch (OverflowException)
            {
                throw;
            }
        }

        public static sbyte ParseByte(string value)
        {
            return TryParseSpecialByte(value) ??
                sbyte.Parse(value);
        }

        private void Expect(char c)
        {
            Reader.SkipWhitespace();
            Reader.Expect(c);
        }
    }

    public class StringReader
    {
        private const char ESCAPE = '\\';
        private const char DOUBLE_QUOTE = '"';
        private const char SINGLE_QUOTE = '\'';
        private readonly string String;
        public int Cursor { get; private set; }
        
        // Optimized: Reusable StringBuilder to avoid string allocations
        private readonly StringBuilder _stringBuilder = new StringBuilder(32);
        
        // Optimized: Cache for common short strings (numbers 0-255, common tokens)
        private static readonly Dictionary<string, string> _stringCache = new Dictionary<string, string>();
        private static readonly object _cacheLock = new object();
        
        static StringReader()
        {
            // Pre-populate cache with common values
            for (int i = 0; i <= 255; i++)
            {
                var str = i.ToString();
                _stringCache[str] = str;
            }
            
            // Add common boolean and special values
            _stringCache["true"] = "true";
            _stringCache["false"] = "false";
            _stringCache["null"] = "null";
            _stringCache["Infinity"] = "Infinity";
            _stringCache["-Infinity"] = "-Infinity";
            _stringCache["NaN"] = "NaN";
        }

        public StringReader(string str)
        {
            String = str;
        }

        public static bool IsQuote(char c)
        {
            return c == DOUBLE_QUOTE || c == SINGLE_QUOTE;
        }

        public static bool UnquotedAllowed(char c)
        {
            return c >= '0' && c <= '9'
                || c >= 'A' && c <= 'Z'
                || c >= 'a' && c <= 'z'
                || c == '_' || c == '-'
                || c == '.' || c == '+'
                || c == '∞';
        }

        public bool CanRead(int length = 1)
        {
            return Cursor + length <= String.Length;
        }

        public char Peek(int offset = 0)
        {
            return String[Cursor + offset];
        }

        public char Read()
        {
            char result = Peek();
            Cursor++;
            return result;
        }

        public string ReadString()
        {
            if (!CanRead())
                return String.Empty;
            char next = Peek();
            if (IsQuote(next))
            {
                Read();
                return ReadStringUntil(next);
            }
            return ReadUnquotedString();
        }

        public string ReadStringUntil(char end)
        {
            var result = new StringBuilder();
            bool escaped = false;
            while (CanRead())
            {
                char c = Read();
                if (escaped)
                {
                    if (c == end || c == ESCAPE)
                    {
                        result.Append(c);
                        escaped = false;
                    }
                    else if (c == 'n')
                    {
                        result.Append('\n');
                        escaped = false;
                    }
                    else
                    {
                        Cursor--;
                        throw new FormatException($"Tried to escape '{c}' at position {Cursor}, which is not allowed");
                    }
                }
                else if (c == ESCAPE)
                    escaped = true;
                else if (c == end)
                    return result.ToString();
                else
                    result.Append(c);
            }
            throw new FormatException($"Expected the string to end with '{end}', but reached end of data");
        }

        public string ReadUnquotedString()
        {
            // Optimized: Use StringBuilder instead of String.Substring to avoid memory allocations
            _stringBuilder.Clear();
            
            while (CanRead() && UnquotedAllowed(Peek()))
            {
                _stringBuilder.Append(Read());
            }
            
            var result = _stringBuilder.ToString();
            
            // Optimized: Check cache for common strings to reduce allocations
            if (result.Length <= 3) // Only cache short strings
            {
                lock (_cacheLock)
                {
                    if (_stringCache.TryGetValue(result, out var cached))
                        return cached;
                    
                    // Cache new short strings (with size limit)
                    if (_stringCache.Count < 1000)
                    {
                        _stringCache[result] = result;
                    }
                }
            }
            
            return result;
        }

        public string ReadQuotedString()
        {
            if (!CanRead())
                return String.Empty;
            char next = Peek();
            if (!IsQuote(next))
                throw new FormatException($"Expected the string to at position {Cursor} to be quoted, but got '{next}'");
            Read();
            return ReadStringUntil(next);
        }

        public void SkipWhitespace()
        {
            while (CanRead() && Char.IsWhiteSpace(Peek()))
            {
                Read();
            }
        }

        public void Expect(char c)
        {
            if (!CanRead())
                throw new FormatException($"Expected '{c}' at position {Cursor}, but reached end of data");
            char read = Read();
            if (read != c)
                throw new FormatException($"Expected '{c}' at position {Cursor}, but got '{read}'");
        }
    }
}
