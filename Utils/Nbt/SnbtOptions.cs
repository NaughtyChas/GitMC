using System.Text.RegularExpressions;

namespace GitMC.Utils.Nbt;

public class SnbtOptions
{
    public enum QuoteMode
    {
        Automatic,
        DoubleQuotes,
        SingleQuotes
    }

    private static readonly Regex StringRegex = new("^[A-Za-z0-9._+-]+$", RegexOptions.Compiled);
    public bool Minified { get; set; }
    public bool IsJsonLike { get; set; }
    public Predicate<string> ShouldQuoteKeys { get; set; } = _ => true;
    public Predicate<string> ShouldQuoteStrings { get; set; } = _ => true;
    public QuoteMode KeyQuoteMode { get; set; }
    public QuoteMode StringQuoteMode { get; set; }
    public bool NumberSuffixes { get; set; }
    public bool ArrayPrefixes { get; set; }
    public INewlineHandler NewlineHandling { get; set; } = EscapeHandler.Instance;

    public static SnbtOptions Default => new()
    {
        Minified = true,
        ShouldQuoteKeys = x => !StringRegex.IsMatch(x),
        ShouldQuoteStrings = _ => true,
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
        ShouldQuoteKeys = _ => true,
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
        ShouldQuoteKeys = _ => false,
        ShouldQuoteStrings = _ => false,
        NumberSuffixes = false,
        ArrayPrefixes = false,
        NewlineHandling = new ReplaceHandler("⏎")
    };

    public static SnbtOptions MultilinePreview => Preview.WithHandler(IgnoreHandler.Instance);

    public SnbtOptions Expanded()
    {
        Minified = false;
        return this;
    }

    public SnbtOptions WithHandler(INewlineHandler handler)
    {
        NewlineHandling = handler;
        return this;
    }

    public interface INewlineHandler
    {
        string Handle();
    }

    public class IgnoreHandler : INewlineHandler
    {
        public static readonly IgnoreHandler Instance = new();

        public string Handle()
        {
            return Environment.NewLine;
        }
    }

    public class EscapeHandler : INewlineHandler
    {
        public static readonly EscapeHandler Instance = new();

        public string Handle()
        {
            return "\\" + "n";
        }
    }

    public class ReplaceHandler : INewlineHandler
    {
        public readonly string Replacement;

        public ReplaceHandler(string replacement)
        {
            Replacement = replacement;
        }

        public string Handle()
        {
            return Replacement;
        }
    }
}