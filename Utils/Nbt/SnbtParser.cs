using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using fNbt;

namespace GitMC.Utils.Nbt
{
    public class SnbtParser
    {
        private static readonly Regex DoublePatternNosuffix = new("^([-+]?(?:[0-9]+[.]|[0-9]*[.][0-9]+)(?:e[-+]?[0-9]+)?)$", RegexOptions.IgnoreCase);
        private static readonly Regex DoublePattern = new("^([-+]?(?:[0-9]+[.]?|[0-9]*[.][0-9]+)(?:e[-+]?[0-9]+)?d)$", RegexOptions.IgnoreCase);
        private static readonly Regex FloatPattern = new("^([-+]?(?:[0-9]+[.]?|[0-9]*[.][0-9]+)(?:e[-+]?[0-9]+)?f)$", RegexOptions.IgnoreCase);
        private static readonly Regex BytePattern = new("^([-+]?(?:0|[1-9][0-9]*)b)$", RegexOptions.IgnoreCase);
        private static readonly Regex LongPattern = new("^([-+]?(?:0|[1-9][0-9]*)l)$", RegexOptions.IgnoreCase);
        private static readonly Regex ShortPattern = new("^([-+]?(?:0|[1-9][0-9]*)s)$", RegexOptions.IgnoreCase);
        private static readonly Regex IntPattern = new("^([-+]?(?:0|[1-9][0-9]*))$");

        // NBT object cache for frequently created values with LRU eviction
        private static readonly LruCache<string, NbtTag> NbtCache = new(MaxNbtCacheSize);
        private const int MaxNbtCacheSize = 10000;

        private readonly StringReader _reader;

        public static Failable<NbtTag> TryParse(string snbt, bool named)
        {
            return new Failable<NbtTag>(() => Parse(snbt, named), "Parse SNBT");
        }

        public static NbtTag Parse(string snbt, bool named)
        {
            // Add check for empty or whitespace strings
            if (string.IsNullOrWhiteSpace(snbt))
            {
                if (named)
                    throw new ArgumentException("Cannot parse empty or whitespace string as named NBT tag");
                return new NbtCompound(); // For unnamed tags, return an empty compound tag instead of throwing an exception
            }
            
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
            _reader = new StringReader(snbt);
        }

        private NbtTag ReadValue()
        {
            _reader.SkipWhitespace();
            char next = _reader.Peek();
            if (next == Snbt.CompoundOpen)
                return ReadCompound();
            if (next == Snbt.ListOpen)
                return ReadListLike();
            return ReadTypedValue();
        }

        private NbtTag ReadNamedValue()
        {
            string key = ReadKey();
            Expect(Snbt.NameValueSeparator);
            NbtTag value = ReadValue();
            value.Name = key;
            return value;
        }

        private void Finish()
        {
            if (_reader.CanRead())
            {
                // Console.WriteLine($"Warning: Trailing data found at position {Reader.Cursor}, but ignored");
            }
        }

        private NbtCompound ReadCompound()
        {
            Expect(Snbt.CompoundOpen);
            _reader.SkipWhitespace();
            var compound = new NbtCompound();
            while (_reader.CanRead() && _reader.Peek() != Snbt.CompoundClose)
            {
                var value = ReadNamedValue();
                compound.Add(value);
                if (!ReadSeparator())
                    break;
            }
            Expect(Snbt.CompoundClose);
            return compound;
        }

        private bool ReadSeparator()
        {
            _reader.SkipWhitespace();
            if (_reader.CanRead() && _reader.Peek() == Snbt.ValueSeparator)
            {
                _reader.Read();
                _reader.SkipWhitespace();
                return true;
            }
            return false;
        }

        private string ReadKey()
        {
            _reader.SkipWhitespace();
            if (!_reader.CanRead())
                throw new FormatException("Expected a key, but reached end of data");
            return _reader.ReadString();
        }

        private NbtTag ReadListLike()
        {
            if (_reader.CanRead(3) && !StringReader.IsQuote(_reader.Peek(1)) && _reader.Peek(2) == Snbt.ArrayDelimiter)
                return ReadArray();
            return ReadList();
        }

        private NbtTag ReadArray()
        {
            Expect(Snbt.ListOpen);
            char type = _reader.Read();
            _reader.Read(); // skip semicolon
            _reader.SkipWhitespace();
            if (!_reader.CanRead())
                throw new FormatException("Expected array to end, but reached end of data");
            if (type == Snbt.ByteArrayPrefix)
                return ReadArray(NbtTagType.Byte);
            if (type == Snbt.LongArrayPrefix)
                return ReadArray(NbtTagType.Long);
            if (type == Snbt.IntArrayPrefix)
                return ReadArray(NbtTagType.Int);
            throw new FormatException($"'{type}' is not a valid array type ({Snbt.ByteArrayPrefix}, {Snbt.LongArrayPrefix}, or {Snbt.IntArrayPrefix})");
        }

        private NbtTag ReadArray(NbtTagType arraytype)
        {
            // Optimized: Use specialized arrays and direct value parsing to avoid creating temporary NBT objects
            if (arraytype == NbtTagType.Byte)
            {
                var list = new List<byte>();
                while (_reader.Peek() != Snbt.ListClose)
                {
                    var value = ReadDirectByteValue();
                    list.Add(value);
                    if (!ReadSeparator())
                        break;
                }
                Expect(Snbt.ListClose);
                return new NbtByteArray(list.ToArray());
            }

            if (arraytype == NbtTagType.Long)
            {
                var list = new List<long>();
                while (_reader.Peek() != Snbt.ListClose)
                {
                    var value = ReadDirectLongValue();
                    list.Add(value);
                    if (!ReadSeparator())
                        break;
                }
                Expect(Snbt.ListClose);
                return new NbtLongArray(list.ToArray());
            }
            else // Int array
            {
                var list = new List<int>();
                while (_reader.Peek() != Snbt.ListClose)
                {
                    var value = ReadDirectIntValue();
                    list.Add(value);
                    if (!ReadSeparator())
                        break;
                }
                Expect(Snbt.ListClose);
                return new NbtIntArray(list.ToArray());
            }
        }

        private NbtList ReadList()
        {
            Expect(Snbt.ListOpen);
            _reader.SkipWhitespace();
            if (!_reader.CanRead())
                throw new FormatException("Expected list to end, but reached end of data");
            
            // Check if this is an empty list
            if (_reader.Peek() == Snbt.ListClose)
            {
                Expect(Snbt.ListClose);
                // For empty lists, use Compound as default type since it's the most common in Minecraft NBT
                return new NbtList(NbtTagType.Compound);
            }
            
            var list = new NbtList();
            while (_reader.Peek() != Snbt.ListClose)
            {
                var tag = ReadValue();
                list.Add(tag);
                if (!ReadSeparator())
                    break;
            }
            Expect(Snbt.ListClose);
            return list;
        }

        private NbtTag ReadTypedValue()
        {
            _reader.SkipWhitespace();
            if (StringReader.IsQuote(_reader.Peek()))
                return new NbtString(_reader.ReadQuotedString());
            string str = _reader.ReadUnquotedString();
            if (str == "")
            {
                // Handle null string
                return new NbtString("");
            }
            return TypeTag(str);
        }

        private NbtTag TypeTag(string str)
        {
            // Check cache first for performance - but we need to clone cached objects
            // because fNbt doesn't allow the same object instance to be in multiple containers
            if (NbtCache.TryGetValue(str, out var cached))
                return CloneNbtTag(cached);
            
            NbtTag result;
            try
            {
                // Optimized: Use spans to avoid creating substring allocations
                ReadOnlySpan<char> span = str.AsSpan();
                ReadOnlySpan<char> valueSpan = span.Length > 1 ? span[..^1] : span; // All except last char for suffixed types
                
                if (FloatPattern.IsMatch(str))
                    result = new NbtFloat(float.Parse(valueSpan, NumberStyles.Float, CultureInfo.InvariantCulture));
                else if (BytePattern.IsMatch(str))
                    result = new NbtByte((byte)sbyte.Parse(valueSpan));
                else if (LongPattern.IsMatch(str))
                    result = new NbtLong(long.Parse(valueSpan));
                else if (ShortPattern.IsMatch(str))
                    result = new NbtShort(short.Parse(valueSpan));
                else if (IntPattern.IsMatch(str))
                    result = new NbtInt(int.Parse(span));
                else if (DoublePattern.IsMatch(str))
                    result = new NbtDouble(double.Parse(valueSpan, NumberStyles.Float, CultureInfo.InvariantCulture));
                else if (DoublePatternNosuffix.IsMatch(str))
                    result = new NbtDouble(double.Parse(span, NumberStyles.Float, CultureInfo.InvariantCulture));
                else
                {
                    var special = SpecialCase(str);
                    if (special != null)
                        result = special;
                    else
                        result = new NbtString(str);
                }
            }
            catch (FormatException)
            {
                result = new NbtString(str);
            }
            catch (OverflowException)
            {
                result = new NbtString(str);
            }
            
            // Cache the result if cache is not too large and string is reasonable length
            if (str.Length <= 100) // Only cache reasonably short strings
            {
                NbtCache.TryAdd(str, result);
            }
            
            return result;
        }

        // Helper method to clone NBT tags to avoid "tag can only be added once" errors
        private static NbtTag CloneNbtTag(NbtTag original)
        {
            // IMPORTANT: When cloning for list usage, we need to clear the name
            // because fNbt lists can only contain unnamed tags
            return original switch
            {
                NbtByte nbtByte => new NbtByte(nbtByte.Value),
                NbtShort nbtShort => new NbtShort(nbtShort.Value),
                NbtInt nbtInt => new NbtInt(nbtInt.Value),
                NbtLong nbtLong => new NbtLong(nbtLong.Value),
                NbtFloat nbtFloat => new NbtFloat(nbtFloat.Value),
                NbtDouble nbtDouble => new NbtDouble(nbtDouble.Value),
                NbtString nbtString => new NbtString(nbtString.Value),
                // For other types, just create new instances (shouldn't happen in TypeTag context)
                _ => original
            };
        }

        private NbtTag? SpecialCase(string text)
        {
            if (String.IsNullOrEmpty(text))
                return null;
            
            // Optimized: Handle common special cases first to avoid DataUtils calls
            ReadOnlySpan<char> span = text.AsSpan();
            
            // Handle suffix cases with span operations
            if (text[^1] == Snbt.FloatSuffix)
            {
                ReadOnlySpan<char> valueSpan = span[..^1];
                
                // Check for common special float values directly without string allocation
                if (valueSpan.SequenceEqual("Infinity".AsSpan()) || valueSpan.SequenceEqual("∞".AsSpan()))
                    return new NbtFloat(float.PositiveInfinity);
                if (valueSpan.SequenceEqual("-Infinity".AsSpan()) || valueSpan.SequenceEqual("-∞".AsSpan()))
                    return new NbtFloat(float.NegativeInfinity);
                if (valueSpan.SequenceEqual("NaN".AsSpan()))
                    return new NbtFloat(float.NaN);
                
                // Fallback to DataUtils for other special cases (if any)
                var specialFloat = DataUtils.TryParseSpecialFloat(valueSpan.ToString());
                if (specialFloat != null)
                    return new NbtFloat(specialFloat.Value);
            }
            if (text[^1] == Snbt.DoubleSuffix)
            {
                ReadOnlySpan<char> valueSpan = span[..^1];
                
                // Check for common special double values directly without string allocation
                if (valueSpan.SequenceEqual("Infinity".AsSpan()) || valueSpan.SequenceEqual("∞".AsSpan()))
                    return new NbtDouble(double.PositiveInfinity);
                if (valueSpan.SequenceEqual("-Infinity".AsSpan()) || valueSpan.SequenceEqual("-∞".AsSpan()))
                    return new NbtDouble(double.NegativeInfinity);
                if (valueSpan.SequenceEqual("NaN".AsSpan()))
                    return new NbtDouble(double.NaN);
                    
                // Fallback to DataUtils for other special cases (if any)
                var specialDouble = DataUtils.TryParseSpecialFloat(valueSpan.ToString());
                if (specialDouble != null)
                    return new NbtDouble(specialDouble.Value);
            }
            
            // Handle non-suffix special cases
            if (span.SequenceEqual("Infinity".AsSpan()) || span.SequenceEqual("∞".AsSpan()))
                return new NbtDouble(double.PositiveInfinity);
            if (span.SequenceEqual("-Infinity".AsSpan()) || span.SequenceEqual("-∞".AsSpan()))
                return new NbtDouble(double.NegativeInfinity);
            if (span.SequenceEqual("NaN".AsSpan()))
                return new NbtDouble(double.NaN);
            
            var specialByte = TryParseSpecialByte(text);
            if (specialByte != null)
                return new NbtByte((byte)specialByte);
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
            _reader.SkipWhitespace();
            if (StringReader.IsQuote(_reader.Peek()))
                throw new FormatException("Array values cannot be quoted strings");
            
            // Parse directly from Reader without creating intermediate strings
            int start = _reader.Cursor;
            
            // Find end of unquoted string
            while (_reader.CanRead() && StringReader.UnquotedAllowed(_reader.Peek()))
            {
                _reader.Read();
            }
            
            int length = _reader.Cursor - start;
            if (length == 0)
                throw new FormatException("Expected byte value to be non-empty");

            ReadOnlySpan<char> valueSpan = _reader.String.AsSpan(start, length);
            
            try
            {
                // Handle special boolean cases
                if (valueSpan.SequenceEqual("true".AsSpan()) || valueSpan.SequenceEqual("TRUE".AsSpan()))
                    return 1;
                if (valueSpan.SequenceEqual("false".AsSpan()) || valueSpan.SequenceEqual("FALSE".AsSpan()))
                    return 0;

                // Handle byte suffix - parse without the 'b' suffix
                if (length > 1 && (valueSpan[^1] == 'b' || valueSpan[^1] == 'B'))
                {
                    ReadOnlySpan<char> numberSpan = valueSpan[..^1];
                    return (byte)sbyte.Parse(numberSpan);
                }
                
                // Handle plain integer that fits in byte range
                var intVal = int.Parse(valueSpan);
                if (intVal >= sbyte.MinValue && intVal <= sbyte.MaxValue)
                    return (byte)(sbyte)intVal;
                throw new OverflowException($"Value {intVal} is out of range for byte");
            }
            catch (FormatException)
            {
                throw new FormatException($"'{valueSpan.ToString()}' is not a valid byte value");
            }
        }

        private long ReadDirectLongValue()
        {
            _reader.SkipWhitespace();
            if (StringReader.IsQuote(_reader.Peek()))
                throw new FormatException("Array values cannot be quoted strings");
            
            // Parse directly from Reader without creating intermediate strings
            int start = _reader.Cursor;
            
            // Find end of unquoted string
            while (_reader.CanRead() && StringReader.UnquotedAllowed(_reader.Peek()))
            {
                _reader.Read();
            }
            
            int length = _reader.Cursor - start;
            if (length == 0)
                throw new FormatException("Expected long value to be non-empty");

            ReadOnlySpan<char> valueSpan = _reader.String.AsSpan(start, length);

            try
            {
                // Handle long suffix - parse without the 'l' suffix
                if (length > 1 && (valueSpan[^1] == 'l' || valueSpan[^1] == 'L'))
                {
                    ReadOnlySpan<char> numberSpan = valueSpan[..^1];
                    return long.Parse(numberSpan);
                }
                
                // Handle plain integer
                return long.Parse(valueSpan);
            }
            catch (FormatException)
            {
                throw new FormatException($"'{valueSpan.ToString()}' is not a valid long value");
            }
        }

        private int ReadDirectIntValue()
        {
            _reader.SkipWhitespace();
            if (StringReader.IsQuote(_reader.Peek()))
                throw new FormatException("Array values cannot be quoted strings");
            
            // Parse directly from Reader without creating intermediate strings
            int start = _reader.Cursor;
            
            // Find end of unquoted string
            while (_reader.CanRead() && StringReader.UnquotedAllowed(_reader.Peek()))
            {
                _reader.Read();
            }
            
            int length = _reader.Cursor - start;
            if (length == 0)
                throw new FormatException("Expected int value to be non-empty");

            ReadOnlySpan<char> valueSpan = _reader.String.AsSpan(start, length);

            try
            {
                // Handle plain integer
                return int.Parse(valueSpan);
            }
            catch (FormatException)
            {
                throw new FormatException($"'{valueSpan.ToString()}' is not a valid int value");
            }
        }

        public static sbyte ParseByte(string value)
        {
            return TryParseSpecialByte(value) ??
                sbyte.Parse(value);
        }

        private void Expect(char c)
        {
            _reader.SkipWhitespace();
            _reader.Expect(c);
        }
    }

    /// <summary>
    /// Thread-safe LRU cache implementation with size limit and automatic eviction
    /// </summary>
    internal class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _maxSize;
        private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _cache;
        private readonly LinkedList<CacheItem> _lruList;
        private readonly object _lock = new object();
        
        private struct CacheItem
        {
            public TKey Key;
            public TValue Value;
            
            public CacheItem(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }
        }
        
        public LruCache(int maxSize)
        {
            _maxSize = maxSize;
            _cache = new Dictionary<TKey, LinkedListNode<CacheItem>>(maxSize);
            _lruList = new LinkedList<CacheItem>();
        }
        
        public bool TryGetValue(TKey key, out TValue value)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var node))
                {
                    // Move to front (most recently used)
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }
                
                value = default!;
                return false;
            }
        }
        
        public void TryAdd(TKey key, TValue value)
        {
            lock (_lock)
            {
                // Don't add if already exists
                if (_cache.ContainsKey(key))
                    return;
                
                // If at capacity, remove least recently used item
                if (_cache.Count >= _maxSize)
                {
                    var lastNode = _lruList.Last;
                    if (lastNode != null)
                    {
                        _cache.Remove(lastNode.Value.Key);
                        _lruList.RemoveLast();
                    }
                }
                
                // Add new item to front
                var newItem = new CacheItem(key, value);
                var newNode = _lruList.AddFirst(newItem);
                _cache[key] = newNode;
            }
        }
        
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _cache.Count;
                }
            }
        }
        
        /// <summary>
        /// Clear all cached items
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _cache.Clear();
                _lruList.Clear();
            }
        }
    }

    public class StringReader
    {
        private const char ESCAPE = '\\';
        private const char DOUBLE_QUOTE = '"';
        private const char SINGLE_QUOTE = '\'';
        public readonly string String;
        public int Cursor { get; private set; }
        
        // Optimized: Reusable StringBuilder to avoid string allocations
        private readonly StringBuilder _stringBuilder = new StringBuilder(32);
        
        // Optimized: Cache for common short strings (numbers 0-255, common tokens)
        private static readonly Dictionary<string, string> StringCache = new Dictionary<string, string>();
        private static readonly object CacheLock = new object();
        
        static StringReader()
        {
            // Pre-populate cache with common values
            for (int i = 0; i <= 255; i++)
            {
                var str = i.ToString();
                StringCache[str] = str;
            }
            
            // Add common boolean and special values
            StringCache["true"] = "true";
            StringCache["false"] = "false";
            StringCache["null"] = "null";
            StringCache["Infinity"] = "Infinity";
            StringCache["-Infinity"] = "-Infinity";
            StringCache["NaN"] = "NaN";
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
            // Optimized: Use the shared StringBuilder instead of creating new instances
            _stringBuilder.Clear();
            bool escaped = false;
            while (CanRead())
            {
                char c = Read();
                if (escaped)
                {
                    if (c == end || c == ESCAPE)
                    {
                        _stringBuilder.Append(c);
                        escaped = false;
                    }
                    else if (c == 'n')
                    {
                        _stringBuilder.Append('\n');
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
                    return _stringBuilder.ToString();
                else
                    _stringBuilder.Append(c);
            }
            throw new FormatException($"Expected the string to end with '{end}', but reached end of data");
        }

        public string ReadUnquotedString()
        {
            int start = Cursor;
            
            // First pass: find the end position without building strings
            while (CanRead() && UnquotedAllowed(Peek()))
            {
                Read();
            }
            
            int length = Cursor - start;
            if (length == 0)
                return string.Empty;
            
            // Optimized: Use string interning for very common short strings to avoid repeated allocations
            // For numbers 0-255 and common tokens, check cache first without creating strings
            if (length <= 3)
            {
                // Try to match common patterns directly without creating intermediate strings
                ReadOnlySpan<char> shortSpan = String.AsSpan(start, length);
                
                // For single characters, use direct comparison
                if (length == 1)
                {
                    char c = shortSpan[0];
                    if (c >= '0' && c <= '9')
                    {
                        int digit = c - '0';
                        lock (CacheLock)
                        {
                            return StringCache[digit.ToString()];
                        }
                    }
                }
                
                // For 2-3 digit numbers, try direct lookup
                if (length <= 3 && IsAllDigits(shortSpan))
                {
                    // Parse the number directly from span to avoid string creation
                    int number = 0;
                    for (int i = 0; i < shortSpan.Length; i++)
                    {
                        number = number * 10 + (shortSpan[i] - '0');
                    }
                    
                    if (number <= 255)
                    {
                        lock (CacheLock)
                        {
                            return StringCache[number.ToString()];
                        }
                    }
                }
                
                // For other short strings, use StringBuilder to avoid span.ToString()
                _stringBuilder.Clear();
                _stringBuilder.EnsureCapacity(length);
                for (int i = 0; i < shortSpan.Length; i++)
                {
                    _stringBuilder.Append(shortSpan[i]);
                }
                string result = _stringBuilder.ToString();
                
                lock (CacheLock)
                {
                    if (StringCache.TryGetValue(result, out var cached))
                        return cached;
                    
                    // Cache new short strings (with size limit)
                    if (StringCache.Count < 1000)
                    {
                        StringCache[result] = result;
                        return result;
                    }
                }
                return result;
            }
            
            // For longer strings, use StringBuilder to minimize memory allocations
            _stringBuilder.Clear();
            _stringBuilder.EnsureCapacity(length);
            
            for (int i = start; i < start + length; i++)
            {
                _stringBuilder.Append(String[i]);
            }
            
            return _stringBuilder.ToString();
        }
        
        // Helper method to check if span contains only digits
        private static bool IsAllDigits(ReadOnlySpan<char> span)
        {
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] < '0' || span[i] > '9')
                    return false;
            }
            return true;
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
