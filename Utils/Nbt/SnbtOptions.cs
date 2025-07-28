using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using fNbt;
using GitMC.Utils;

namespace GitMC.Utils.Nbt
{
    public class SnbtOptions
    {
        public bool Minified { get; set; }
        public bool IsJsonLike { get; set; }
        public Predicate<string> ShouldQuoteKeys { get; set; } = _ => true;
        public Predicate<string> ShouldQuoteStrings { get; set; } = _ => true;
        public QuoteMode KeyQuoteMode { get; set; }
        public QuoteMode StringQuoteMode { get; set; }
        public bool NumberSuffixes { get; set; }
        public bool ArrayPrefixes { get; set; }
        public INewlineHandler NewlineHandling { get; set; } = EscapeHandler.Instance;

        public enum QuoteMode
        {
            Automatic,
            DoubleQuotes,
            SingleQuotes
        }

        public interface INewlineHandler
        {
            string Handle();
        }

        public class IgnoreHandler : INewlineHandler
        {
            public static readonly IgnoreHandler Instance = new();
            public string Handle() => Environment.NewLine;
        }

        public class EscapeHandler : INewlineHandler
        {
            public static readonly EscapeHandler Instance = new();
            public string Handle() => "\\" + "n";
        }

        public class ReplaceHandler : INewlineHandler
        {
            public readonly string Replacement;
            public ReplaceHandler(string replacement)
            {
                Replacement = replacement;
            }
            public string Handle() => Replacement;
        }

        public SnbtOptions Expanded()
        {
            this.Minified = false;
            return this;
        }

        public SnbtOptions WithHandler(INewlineHandler handler)
        {
            this.NewlineHandling = handler;
            return this;
        }

        private static readonly Regex StringRegex = new("^[A-Za-z0-9._+-]+$", RegexOptions.Compiled);

        public static SnbtOptions Default => new()
        {
            Minified = true,
            ShouldQuoteKeys = x => !StringRegex.IsMatch(x),
            ShouldQuoteStrings = x => true,
            KeyQuoteMode = QuoteMode.Automatic,
            StringQuoteMode = QuoteMode.Automatic,
            NumberSuffixes = true,
            ArrayPrefixes = true,
            NewlineHandling = EscapeHandler.Instance
        };
        public static SnbtOptions DefaultExpanded => Default.Expanded();

        public static SnbtOptions JsonLike => new()
        {
            Minified = true,
            IsJsonLike = true,
            ShouldQuoteKeys = x => true,
            ShouldQuoteStrings = x => x != "null",
            KeyQuoteMode = QuoteMode.DoubleQuotes,
            StringQuoteMode = QuoteMode.DoubleQuotes,
            NumberSuffixes = false,
            ArrayPrefixes = false,
            NewlineHandling = EscapeHandler.Instance
        };
        public static SnbtOptions JsonLikeExpanded => JsonLike.Expanded();

        public static SnbtOptions Preview => new()
        {
            Minified = true,
            ShouldQuoteKeys = x => false,
            ShouldQuoteStrings = x => false,
            NumberSuffixes = false,
            ArrayPrefixes = false,
            NewlineHandling = new ReplaceHandler("⏎")
        };

        public static SnbtOptions MultilinePreview => Preview.WithHandler(IgnoreHandler.Instance);
    }
}
