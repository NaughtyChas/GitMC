using System.Globalization;

namespace GitMC.Utils
{
    public static class DataUtils
    {
        private static readonly Dictionary<string, (double d, float f)> SpecialFloatingPoints = new(StringComparer.OrdinalIgnoreCase)
        {
            { "∞", (double.PositiveInfinity, float.PositiveInfinity) },
            { "+∞", (double.PositiveInfinity, float.PositiveInfinity) },
            { "-∞", (double.NegativeInfinity, float.NegativeInfinity) },
            { "Infinity", (double.PositiveInfinity, float.PositiveInfinity) },
            { "+Infinity", (double.PositiveInfinity, float.PositiveInfinity) },
            { "-Infinity", (double.NegativeInfinity, float.NegativeInfinity) },
            { "NaN", (double.NaN, float.NaN) }
        };

        public static double? TryParseSpecialDouble(string value)
        {
            if (SpecialFloatingPoints.TryGetValue(value, out var result))
                return result.d;
            return null;
        }

        public static float? TryParseSpecialFloat(string value)
        {
            if (SpecialFloatingPoints.TryGetValue(value, out var result))
                return result.f;
            return null;
        }

        public static double ParseDouble(string value)
        {
            return TryParseSpecialDouble(value) ??
                double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        public static float ParseFloat(string value)
        {
            return TryParseSpecialFloat(value) ??
                float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        public static string DoubleToString(double d)
        {
            return d.ToString("0." + new string('#', 339), CultureInfo.InvariantCulture);
        }

        public static string FloatToString(float f)
        {
            return f.ToString("0." + new string('#', 339), CultureInfo.InvariantCulture);
        }
    }
}
