using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BackendRequisicionPersonal.Helpers
{
    public static class TextNormalizer
    {
        public static string NormalizeForComparison(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var trimmed = Regex.Replace(input.Trim(), "\\s+", " ");
            var normalized = trimmed.Normalize(NormalizationForm.FormD);

            var sb = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        }
    }
}
