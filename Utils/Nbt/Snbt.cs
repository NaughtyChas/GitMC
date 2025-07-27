using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using fNbt;
using GitMC.Utils;

namespace GitMC.Utils.Nbt
{
    public static class Snbt
    {
        public const char BYTE_SUFFIX = 'b';
        public const char SHORT_SUFFIX = 's';
        public const char LONG_SUFFIX = 'L';
        public const char FLOAT_SUFFIX = 'f';
        public const char DOUBLE_SUFFIX = 'd';
        public const char BYTE_ARRAY_PREFIX = 'B';
        public const char INT_ARRAY_PREFIX = 'I';
        public const char LONG_ARRAY_PREFIX = 'L';
        public const char NAME_VALUE_SEPARATOR = ':';
        public const char VALUE_SEPARATOR = ',';
        public const char ARRAY_DELIMITER = ';';
        public const char LIST_OPEN = '[';
        public const char LIST_CLOSE = ']';
        public const char COMPOUND_OPEN = '{';
        public const char COMPOUND_CLOSE = '}';
        public const char STRING_ESCAPE = '\\';
        public const char STRING_PRIMARY_QUOTE = '"';
        public const char STRING_SECONDARY_QUOTE = '\'';
        public const string VALUE_SPACING = " ";
        public const string INDENTATION = "    ";

        // convert a tag to its string form
        public static string ToSnbt(this NbtTag tag, SnbtOptions options, bool include_name = false)
        {
            string name = include_name ? GetNameBeforeValue(tag, options) : String.Empty;
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

        public static string ToSnbt(this NbtByte tag, SnbtOptions options) => (sbyte)tag.Value + OptionalSuffix(options, BYTE_SUFFIX);
        public static string ToSnbt(this NbtShort tag, SnbtOptions options) => tag.Value + OptionalSuffix(options, SHORT_SUFFIX);
        public static string ToSnbt(this NbtInt tag, SnbtOptions options) => tag.Value.ToString();
        public static string ToSnbt(this NbtLong tag, SnbtOptions options) => tag.Value + OptionalSuffix(options, LONG_SUFFIX);
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
            return result + OptionalSuffix(options, FLOAT_SUFFIX);
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
            return result + OptionalSuffix(options, DOUBLE_SUFFIX);
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
                return ListToString("", x => x.ToSnbt(options, include_name: false), tag, options);
            else
            {
                var sb = new StringBuilder();
                AddSnbtList(tag, options, sb, INDENTATION, 0, false);
                return sb.ToString();
            }
        }

        public static string ToSnbt(this NbtCompound tag, SnbtOptions options)
        {
            var sb = new StringBuilder();
            if (options.Minified)
            {
                sb.Append(COMPOUND_OPEN);
                // Optimized: Use StringBuilder instead of String.Join to avoid creating intermediate arrays
                bool first = true;
                foreach (var childTag in tag)
                {
                    if (!first)
                        sb.Append(VALUE_SEPARATOR);
                    sb.Append(childTag.ToSnbt(options, include_name: true));
                    first = false;
                }
                sb.Append(COMPOUND_CLOSE);
            }
            else
                AddSnbtCompound(tag, options, sb, INDENTATION, 0, false);
            return sb.ToString();
        }

        // shared technique for single-line arrays
        private static string ListToString<T>(string list_prefix, Func<T, string> function, IEnumerable<T> values, SnbtOptions options)
        {
            if (!options.ArrayPrefixes)
                list_prefix = String.Empty;
            // spacing between values
            string spacing = options.Minified ? String.Empty : VALUE_SPACING;
            // spacing between list prefix and first value
            string prefix_separator = !options.Minified && list_prefix.Length > 0 && values.Any() ? VALUE_SPACING : String.Empty;
            var s = new StringBuilder(LIST_OPEN + list_prefix + prefix_separator);
            
            // Optimized: Use StringBuilder instead of String.Join to avoid creating intermediate arrays
            bool first = true;
            foreach (var value in values)
            {
                if (!first)
                {
                    s.Append(VALUE_SEPARATOR);
                    s.Append(spacing);
                }
                s.Append(function(value));
                first = false;
            }
            
            s.Append(LIST_CLOSE);
            return s.ToString();
        }

        private static string QuoteIfRequested(string str, Predicate<string> should_quote, SnbtOptions.QuoteMode mode, SnbtOptions.INewlineHandler newlines)
        {
            if (should_quote(str))
                return QuoteAndEscape(str, mode, newlines);
            else
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
            return GetName(tag, options) + NAME_VALUE_SEPARATOR + (options.Minified ? String.Empty : VALUE_SPACING);
        }

        // adapted directly from minecraft's (decompiled) source
        private static string QuoteAndEscape(string input, SnbtOptions.QuoteMode mode, SnbtOptions.INewlineHandler newlines)
        {
            const char PLACEHOLDER_QUOTE = '\0';
            var builder = new StringBuilder(PLACEHOLDER_QUOTE.ToString()); // dummy value to be replaced at end
            char preferred_quote;
            if (mode == SnbtOptions.QuoteMode.DoubleQuotes)
                preferred_quote = '"';
            else if (mode == SnbtOptions.QuoteMode.SingleQuotes)
                preferred_quote = '\'';
            else
                preferred_quote = PLACEHOLDER_QUOTE; // dummy value when we're not sure which quote type to use yet
            foreach (char c in input)
            {
                if (c == STRING_ESCAPE)
                    builder.Append(STRING_ESCAPE);
                else if (c == STRING_PRIMARY_QUOTE || c == STRING_SECONDARY_QUOTE)
                {
                    if (preferred_quote == PLACEHOLDER_QUOTE)
                        preferred_quote = (c == STRING_PRIMARY_QUOTE ? STRING_SECONDARY_QUOTE : STRING_PRIMARY_QUOTE);
                    if (c == preferred_quote)
                        builder.Append(STRING_ESCAPE);
                }
                if (c == '\n')
                    builder.Append(newlines.Handle());
                else
                    builder.Append(c);
            }
            if (preferred_quote == PLACEHOLDER_QUOTE)
                preferred_quote = STRING_PRIMARY_QUOTE;
            builder[0] = preferred_quote;
            builder.Append(preferred_quote);
            return builder.ToString();
        }

        private static void AddIndents(StringBuilder sb, string indent_string, int indent_level)
        {
            for (int i = 0; i < indent_level; i++)
                sb.Append(indent_string);
        }

        // add contents of tag to stringbuilder
        private static void AddSnbt(NbtTag tag, SnbtOptions options, StringBuilder sb, string indent_string, int indent_level, bool include_name)
        {
            if (tag is NbtCompound compound)
                AddSnbtCompound(compound, options, sb, indent_string, indent_level, include_name);
            else if (tag is NbtList list)
                AddSnbtList(list, options, sb, indent_string, indent_level, include_name);
            else
            {
                AddIndents(sb, indent_string, indent_level);
                sb.Append(tag.ToSnbt(options, include_name));
            }
        }

        private static void AddSnbtCompound(NbtCompound tag, SnbtOptions options, StringBuilder sb, string indent_string, int indent_level, bool include_name)
        {
            AddIndents(sb, indent_string, indent_level);
            if (include_name)
                sb.Append(GetNameBeforeValue(tag, options));
            sb.Append(COMPOUND_OPEN);
            if (tag.Count > 0)
            {
                sb.Append(Environment.NewLine);
                var tags = tag.ToArray();
                for (int i = 0; i < tags.Length; i++)
                {
                    AddSnbt(tags[i], options, sb, indent_string, indent_level + 1, true);
                    if (i < tags.Length - 1)
                        sb.Append(VALUE_SEPARATOR);
                    sb.Append(Environment.NewLine);
                }
                AddIndents(sb, indent_string, indent_level);
            }
            sb.Append(COMPOUND_CLOSE);
        }

        private static void AddSnbtList(NbtList tag, SnbtOptions options, StringBuilder sb, string indent_string, int indent_level, bool include_name)
        {
            AddIndents(sb, indent_string, indent_level);
            if (include_name)
                sb.Append(GetNameBeforeValue(tag, options));
            bool compressed = ShouldCompressListOf(tag.ListType);
            if (compressed)
                sb.Append(ListToString("", x => x.ToSnbt(options, include_name: false), tag, options));
            else
            {
                sb.Append(LIST_OPEN);
                if (tag.Count > 0)
                {
                    sb.Append(Environment.NewLine);
                    for (int i = 0; i < tag.Count; i++)
                    {
                        AddSnbt(tag[i], options, sb, indent_string, indent_level + 1, false);
                        if (i < tag.Count - 1)
                            sb.Append(VALUE_SEPARATOR);
                        sb.Append(Environment.NewLine);
                    }
                    AddIndents(sb, indent_string, indent_level);
                }
                sb.Append(LIST_CLOSE);
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
                sb.Append(LIST_OPEN);
                sb.Append(BYTE_ARRAY_PREFIX);
                sb.Append(ARRAY_DELIMITER);
                if (!options.Minified && array.Length > 0)
                    sb.Append(VALUE_SPACING);
            }
            else
            {
                sb.Append(LIST_OPEN);
            }

            // Add array elements
            string spacing = options.Minified ? String.Empty : VALUE_SPACING;
            string suffix = options.NumberSuffixes ? BYTE_SUFFIX.ToString() : String.Empty;
            
            for (int i = 0; i < array.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(VALUE_SEPARATOR);
                    sb.Append(spacing);
                }
                sb.Append(((sbyte)array[i]).ToString());
                sb.Append(suffix);
            }

            sb.Append(LIST_CLOSE);
            return sb.ToString();
        }

        private static string IntArrayToString(int[] array, SnbtOptions options)
        {
            var sb = new StringBuilder();
            
            // Add array prefix
            if (options.ArrayPrefixes)
            {
                sb.Append(LIST_OPEN);
                sb.Append(INT_ARRAY_PREFIX);
                sb.Append(ARRAY_DELIMITER);
                if (!options.Minified && array.Length > 0)
                    sb.Append(VALUE_SPACING);
            }
            else
            {
                sb.Append(LIST_OPEN);
            }

            // Add array elements
            string spacing = options.Minified ? String.Empty : VALUE_SPACING;
            
            for (int i = 0; i < array.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(VALUE_SEPARATOR);
                    sb.Append(spacing);
                }
                sb.Append(array[i].ToString());
            }

            sb.Append(LIST_CLOSE);
            return sb.ToString();
        }

        private static string LongArrayToString(long[] array, SnbtOptions options)
        {
            var sb = new StringBuilder();
            
            // Add array prefix
            if (options.ArrayPrefixes)
            {
                sb.Append(LIST_OPEN);
                sb.Append(LONG_ARRAY_PREFIX);
                sb.Append(ARRAY_DELIMITER);
                if (!options.Minified && array.Length > 0)
                    sb.Append(VALUE_SPACING);
            }
            else
            {
                sb.Append(LIST_OPEN);
            }

            // Add array elements
            string spacing = options.Minified ? String.Empty : VALUE_SPACING;
            string suffix = options.NumberSuffixes ? LONG_SUFFIX.ToString() : String.Empty;
            
            for (int i = 0; i < array.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(VALUE_SEPARATOR);
                    sb.Append(spacing);
                }
                sb.Append(array[i].ToString());
                sb.Append(suffix);
            }

            sb.Append(LIST_CLOSE);
            return sb.ToString();
        }
    }
}
