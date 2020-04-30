using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Luna.System
{
    public static class StringExtensions
    {
        // thanks: https://stackoverflow.com/questions/31935240/utf-16-safe-substring-in-c-sharp-net
        public static string UnicodeSafeSubstring(this string str, int startIndex, int length)
        {
            if (str == null)
            {
                throw new ArgumentNullException("str");
            }

            if (startIndex < 0 || startIndex > str.Length)
            {
                throw new ArgumentOutOfRangeException("startIndex");
            }

            if (length < 0)
            {
                throw new ArgumentOutOfRangeException("length");
            }

            if (startIndex + length > str.Length)
            {
                throw new ArgumentOutOfRangeException("length");
            }

            if (length == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(length);

            int end = startIndex + length;

            var enumerator = StringInfo.GetTextElementEnumerator(str, startIndex);

            while (enumerator.MoveNext())
            {
                if (startIndex >= end)
                {
                    break;
                }

                string grapheme = enumerator.GetTextElement();
                startIndex += grapheme.Length;

                // Skip initial Low Surrogates/Combining Marks
                if (sb.Length == 0)
                {
                    if (char.IsLowSurrogate(grapheme[0]))
                    {
                        continue;
                    }

                    UnicodeCategory cat = char.GetUnicodeCategory(grapheme, 0);

                    if (cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.SpacingCombiningMark || cat == UnicodeCategory.EnclosingMark)
                    {
                        continue;
                    }
                }

                sb.Append(grapheme);
            }

            return sb.ToString();
        }
    }
}
