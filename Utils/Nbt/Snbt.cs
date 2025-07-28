using System.Text;
using fNbt;

namespace GitMC.Utils.Nbt
{
    public static class Snbt
    {
        public const char ByteSuffix = 'b';
        public const char ShortSuffix = 's';
        public const char LongSuffix = 'L';
        public const char FloatSuffix = 'f';
        public const char DoubleSuffix = 'd';
        public const char ByteArrayPrefix = 'B';
        public const char IntArrayPrefix = 'I';
        public const char LongArrayPrefix = 'L';
        public const char NameValueSeparator = ':';
        public const char ValueSeparator = ',';
        public const char ArrayDelimiter = ';';
        public const char ListOpen = '[';
        public const char ListClose = ']';
        public const char CompoundOpen = '{';
        public const char CompoundClose = '}';
        public const char StringEscape = '\\';
        public const char StringPrimaryQuote = '"';
        public const char StringSecondaryQuote = '\'';
        public const string ValueSpacing = " ";
        public const string Indentation = "    ";

        // convert a tag to its string form
        public static string ToSnbt(this NbtTag tag, SnbtOptions options, bool includeName = false)
        {
            string name = includeName ? GetNameBeforeValue(tag, options) : String.Empty;
            if (tag is NbtByte b)
                return name + b.ToSnbt(options);
            if (tag is NbtShort s)
                return name + s.ToSnbt(options);
            if (tag is NbtInt i)
                return name + i.ToSnbt(options);
            if (tag is NbtLong l)
                return name + l.ToSnbt(options);
            if (tag is NbtFloat f)
                return name + f.ToSnbt(options);
            if (tag is NbtDouble d)
                return name + d.ToSnbt(options);
            if (tag is NbtString str)
                return name + str.ToSnbt(options);
            if (tag is NbtByteArray ba)
                return name + ba.ToSnbt(options);
            if (tag is NbtIntArray ia)
                return name + ia.ToSnbt(options);
            if (tag is NbtLongArray la)
                return name + la.ToSnbt(options);
            if (tag is NbtList list)
                return name + list.ToSnbt(options);
            if (tag is NbtCompound compound)
                return name + compound.ToSnbt(options);
            throw new ArgumentException($"Can't convert tag of type {tag.TagType} to SNBT");
        }

        private static string OptionalSuffix(SnbtOptions options, char suffix)
        {
            return options.NumberSuffixes ? suffix.ToString() : String.Empty;
        }

        public static string ToSnbt(this NbtByte tag, SnbtOptions options) => (sbyte)tag.Value + OptionalSuffix(options, ByteSuffix);
        public static string ToSnbt(this NbtShort tag, SnbtOptions options) => tag.Value + OptionalSuffix(options, ShortSuffix);
        public static string ToSnbt(this NbtInt tag, SnbtOptions options) => tag.Value.ToString();
        public static string ToSnbt(this NbtLong tag, SnbtOptions options) => tag.Value + OptionalSuffix(options, LongSuffix);
        public static string ToSnbt(this NbtFloat tag, SnbtOptions options)
        {
            string result;
            if (float.IsPositiveInfinity(tag.Value))
                result = "Infinity";
            else if (float.IsNegativeInfinity(tag.Value))
                result = "-Infinity";
            else if (float.IsNaN(tag.Value))
                result = "NaN";
            else
                result = DataUtils.FloatToString(tag.Value);
            return result + OptionalSuffix(options, FloatSuffix);
        }
        public static string ToSnbt(this NbtDouble tag, SnbtOptions options)
        {
            string result;
            if (double.IsPositiveInfinity(tag.Value))
                result = "Infinity";
            else if (double.IsNegativeInfinity(tag.Value))
                result = "-Infinity";
            else if (double.IsNaN(tag.Value))
                result = "NaN";
            else
                result = DataUtils.DoubleToString(tag.Value);
            return result + OptionalSuffix(options, DoubleSuffix);
        }
        public static string ToSnbt(this NbtString tag, SnbtOptions options)
        {
            return QuoteIfRequested(tag.Value, options.ShouldQuoteStrings, options.StringQuoteMode, options.NewlineHandling);
        }

        public static string ToSnbt(this NbtByteArray tag, SnbtOptions options)
        {
            return ByteArrayToString(tag.Value, options);
        }

        public static string ToSnbt(this NbtIntArray tag, SnbtOptions options)
        {
            return IntArrayToString(tag.Value, options);
        }

        public static string ToSnbt(this NbtLongArray tag, SnbtOptions options)
        {
            return LongArrayToString(tag.Value, options);
        }

        public static string ToSnbt(this NbtList tag, SnbtOptions options)
        {
            if (options.Minified)
                return ListToString("", x => x.ToSnbt(options, includeName: false), tag, options);
            var sb = new StringBuilder();
            AddSnbtList(tag, options, sb, Indentation, 0, false);
            return sb.ToString();
        }

        public static string ToSnbt(this NbtCompound tag, SnbtOptions options)
        {
            var sb = new StringBuilder();
            if (options.Minified)
            {
                sb.Append(CompoundOpen);
                // Optimized: Use StringBuilder instead of String.Join to avoid creating intermediate arrays
                bool first = true;
                foreach (var childTag in tag)
                {
                    if (!first)
                        sb.Append(ValueSeparator);
                    sb.Append(childTag.ToSnbt(options, includeName: true));
                    first = false;
                }
                sb.Append(CompoundClose);
            }
            else
                AddSnbtCompound(tag, options, sb, Indentation, 0, false);
            return sb.ToString();
        }

        // shared technique for single-line arrays
        private static string ListToString<T>(string listPrefix, Func<T, string> function, IEnumerable<T> values, SnbtOptions options)
        {
            if (!options.ArrayPrefixes)
                listPrefix = String.Empty;
            // spacing between values
            string spacing = options.Minified ? String.Empty : ValueSpacing;
            // spacing between list prefix and first value
            string prefixSeparator = !options.Minified && listPrefix.Length > 0 && values.Any() ? ValueSpacing : String.Empty;
            var s = new StringBuilder(ListOpen + listPrefix + prefixSeparator);
            
            // Optimized: Use StringBuilder instead of String.Join to avoid creating intermediate arrays
            bool first = true;
            foreach (var value in values)
            {
                if (!first)
                {
                    s.Append(ValueSeparator);
                    s.Append(spacing);
                }
                s.Append(function(value));
                first = false;
            }
            
            s.Append(ListClose);
            return s.ToString();
        }

        private static string QuoteIfRequested(string str, Predicate<string> shouldQuote, SnbtOptions.QuoteMode mode, SnbtOptions.INewlineHandler newlines)
        {
            if (shouldQuote(str))
                return QuoteAndEscape(str, mode, newlines);
            return str?.Replace("\n", newlines.Handle()) ?? "";
        }

        public static string GetName(NbtTag tag, SnbtOptions options)
        {
            return QuoteIfRequested(tag.Name ?? "", options.ShouldQuoteKeys, options.KeyQuoteMode, options.NewlineHandling);
        }

        private static string GetNameBeforeValue(NbtTag tag, SnbtOptions options)
        {
            if (tag.Name == null)
                return String.Empty;
            return GetName(tag, options) + NameValueSeparator + (options.Minified ? String.Empty : ValueSpacing);
        }

        // adapted directly from minecraft's (decompiled) source
        private static string QuoteAndEscape(string input, SnbtOptions.QuoteMode mode, SnbtOptions.INewlineHandler newlines)
        {
            const char placeholderQuote = '\0';
            var builder = new StringBuilder(placeholderQuote.ToString()); // dummy value to be replaced at end
            char preferredQuote;
            if (mode == SnbtOptions.QuoteMode.DoubleQuotes)
                preferredQuote = '"';
            else if (mode == SnbtOptions.QuoteMode.SingleQuotes)
                preferredQuote = '\'';
            else
                preferredQuote = placeholderQuote; // dummy value when we're not sure which quote type to use yet
            foreach (char c in input)
            {
                if (c == StringEscape)
                    builder.Append(StringEscape);
                else if (c == StringPrimaryQuote || c == StringSecondaryQuote)
                {
                    if (preferredQuote == placeholderQuote)
                        preferredQuote = (c == StringPrimaryQuote ? StringSecondaryQuote : StringPrimaryQuote);
                    if (c == preferredQuote)
                        builder.Append(StringEscape);
                }
                if (c == '\n')
                    builder.Append(newlines.Handle());
                else
                    builder.Append(c);
            }
            if (preferredQuote == placeholderQuote)
                preferredQuote = StringPrimaryQuote;
            builder[0] = preferredQuote;
            builder.Append(preferredQuote);
            return builder.ToString();
        }

        private static void AddIndents(StringBuilder sb, string indentString, int indentLevel)
        {
            for (int i = 0; i < indentLevel; i++)
                sb.Append(indentString);
        }

        // add contents of tag to stringbuilder
        private static void AddSnbt(NbtTag tag, SnbtOptions options, StringBuilder sb, string indentString, int indentLevel, bool includeName)
        {
            if (tag is NbtCompound compound)
                AddSnbtCompound(compound, options, sb, indentString, indentLevel, includeName);
            else if (tag is NbtList list)
                AddSnbtList(list, options, sb, indentString, indentLevel, includeName);
            else
            {
                AddIndents(sb, indentString, indentLevel);
                sb.Append(tag.ToSnbt(options, includeName));
            }
        }

        private static void AddSnbtCompound(NbtCompound tag, SnbtOptions options, StringBuilder sb, string indentString, int indentLevel, bool includeName)
        {
            AddIndents(sb, indentString, indentLevel);
            if (includeName)
                sb.Append(GetNameBeforeValue(tag, options));
            sb.Append(CompoundOpen);
            if (tag.Count > 0)
            {
                sb.Append(Environment.NewLine);
                var tags = tag.ToArray();
                for (int i = 0; i < tags.Length; i++)
                {
                    AddSnbt(tags[i], options, sb, indentString, indentLevel + 1, true);
                    if (i < tags.Length - 1)
                        sb.Append(ValueSeparator);
                    sb.Append(Environment.NewLine);
                }
                AddIndents(sb, indentString, indentLevel);
            }
            sb.Append(CompoundClose);
        }

        private static void AddSnbtList(NbtList tag, SnbtOptions options, StringBuilder sb, string indentString, int indentLevel, bool includeName)
        {
            AddIndents(sb, indentString, indentLevel);
            if (includeName)
                sb.Append(GetNameBeforeValue(tag, options));
            bool compressed = ShouldCompressListOf(tag.ListType);
            if (compressed)
                sb.Append(ListToString("", x => x.ToSnbt(options, includeName: false), tag, options));
            else
            {
                sb.Append(ListOpen);
                if (tag.Count > 0)
                {
                    sb.Append(Environment.NewLine);
                    for (int i = 0; i < tag.Count; i++)
                    {
                        AddSnbt(tag[i], options, sb, indentString, indentLevel + 1, false);
                        if (i < tag.Count - 1)
                            sb.Append(ValueSeparator);
                        sb.Append(Environment.NewLine);
                    }
                    AddIndents(sb, indentString, indentLevel);
                }
                sb.Append(ListClose);
            }
        }

        // when a multiline list contains this type, should it keep all the values on one line anyway?
        private static bool ShouldCompressListOf(NbtTagType type)
        {
            return type == NbtTagType.Byte || type == NbtTagType.Short || 
                   type == NbtTagType.Int || type == NbtTagType.Long || 
                   type == NbtTagType.Float || type == NbtTagType.Double;
        }

        // Optimized array conversion methods to avoid excessive string allocations
        private static string ByteArrayToString(byte[] array, SnbtOptions options)
        {
            var sb = new StringBuilder();
            
            // Add array prefix
            if (options.ArrayPrefixes)
            {
                sb.Append(ListOpen);
                sb.Append(ByteArrayPrefix);
                sb.Append(ArrayDelimiter);
                if (!options.Minified && array.Length > 0)
                    sb.Append(ValueSpacing);
            }
            else
            {
                sb.Append(ListOpen);
            }

            // Add array elements
            string spacing = options.Minified ? String.Empty : ValueSpacing;
            string suffix = options.NumberSuffixes ? ByteSuffix.ToString() : String.Empty;
            
            for (int i = 0; i < array.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(ValueSeparator);
                    sb.Append(spacing);
                }
                sb.Append(((sbyte)array[i]).ToString());
                sb.Append(suffix);
            }

            sb.Append(ListClose);
            return sb.ToString();
        }

        private static string IntArrayToString(int[] array, SnbtOptions options)
        {
            var sb = new StringBuilder();
            
            // Add array prefix
            if (options.ArrayPrefixes)
            {
                sb.Append(ListOpen);
                sb.Append(IntArrayPrefix);
                sb.Append(ArrayDelimiter);
                if (!options.Minified && array.Length > 0)
                    sb.Append(ValueSpacing);
            }
            else
            {
                sb.Append(ListOpen);
            }

            // Add array elements
            string spacing = options.Minified ? String.Empty : ValueSpacing;
            
            for (int i = 0; i < array.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(ValueSeparator);
                    sb.Append(spacing);
                }
                sb.Append(array[i].ToString());
            }

            sb.Append(ListClose);
            return sb.ToString();
        }

        private static string LongArrayToString(long[] array, SnbtOptions options)
        {
            var sb = new StringBuilder();
            
            // Add array prefix
            if (options.ArrayPrefixes)
            {
                sb.Append(ListOpen);
                sb.Append(LongArrayPrefix);
                sb.Append(ArrayDelimiter);
                if (!options.Minified && array.Length > 0)
                    sb.Append(ValueSpacing);
            }
            else
            {
                sb.Append(ListOpen);
            }

            // Add array elements
            string spacing = options.Minified ? String.Empty : ValueSpacing;
            string suffix = options.NumberSuffixes ? LongSuffix.ToString() : String.Empty;
            
            for (int i = 0; i < array.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(ValueSeparator);
                    sb.Append(spacing);
                }
                sb.Append(array[i].ToString());
                sb.Append(suffix);
            }

            sb.Append(ListClose);
            return sb.ToString();
        }
    }
}
