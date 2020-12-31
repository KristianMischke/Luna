using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace LingK
{
    public class BasicTokenizer
    {
        public const string START_GRAM= "<S>";
        public const string END_GRAM = "<E>";

        public const string LINK_GRAM = "%%LINK%%";
        public const string NUMBER_GRAM = "%%NUM-X%%";
        public const string NUMBER_GRAM_PATTERN = "%%NUM-(?<count>\\d+)%%";
        public const string USER_GRAM = "%%USER%%";

        private const string LINK_PATTERN = @"((?:http|ftp)s?:\/\/[^\s]*)";
        private const string SPLIT_PATTERN = @"(```)|(`)|(~~)|(!+)|($+)|(\.+)|(\?+)|([%^&])|(\*\*\*)|(\*\*)|(\*)|(\()|(\))|(__)|(_ )|(_)|([+\-={}[\]\\"",/<;:\|])|(>>>)|(>)|(\n)|(\r)|(\t)|( +)";
        private const string NUMBER_PATTERN = @"(\d+)";
        private static readonly Regex linksRegex = new Regex(LINK_PATTERN);
        private static readonly Regex transitionRegex = new Regex($"^({SPLIT_PATTERN})$");
        private static readonly Regex numberRegex = new Regex($"^{NUMBER_PATTERN}$");
        private static readonly Regex numberPlaceholderRegex = new Regex(NUMBER_GRAM_PATTERN);
        private static readonly Regex splitAndLinkRegex = new Regex($"(<:\\w+:\\d+>)|{LINK_PATTERN}|({USER_GRAM})|{NUMBER_PATTERN}|{SPLIT_PATTERN}");

        public static T Identity<T>(T value) => value;

        static Regex bigramRegex = new Regex(@"^\((?<Item1>((?!, ).)*), (?<Item2>((?!, ).)*)\)$");
        public static (string, string) ParseBigram(string value)
        {
            Match m = bigramRegex.Match(value);
            return (m.Groups["Item1"].Value, m.Groups["Item2"].Value);
        }
        static Regex trigramRegex = new Regex(@"^\((?<Item1>((?!, ).)*), (?<Item2>((?!, ).)*), (?<Item3>((?!, ).)*)\)$");
        public static (string, string, string) ParseTrigram(string value)
        {
            Match m = trigramRegex.Match(value);
            return (m.Groups["Item1"].Value, m.Groups["Item2"].Value, m.Groups["Item3"].Value);
        }

        public static List<string> Tokenize(string text, List<string> outLinks = null)
        {
            if (text == null)
                return null;

            string[] tokens = splitAndLinkRegex.Split(text);
            List<string> result = new List<string>();

            for (int i = 0; i < tokens.Length; i++)
            {
                if (linksRegex.IsMatch(tokens[i]))
                {
                    outLinks?.Add(tokens[i]);
                    tokens[i] = LINK_GRAM;
                }
                else if (numberRegex.IsMatch(tokens[i]))
                {
                    tokens[i] = NUMBER_GRAM.Replace("X", tokens[i].Length.ToString());
                }

                if (!string.IsNullOrEmpty(tokens[i]))
                {
                    result.Add(tokens[i]);
                }
            }

            return result;
        }

        //maybe not best place for this
        public static void LoadGramHelper(List<string> tokens, string column, bool includeSpaceUnigram, ICoOccurrenceMatrix<string, string, int> unigramMatrix, ICoOccurrenceMatrix<(string, string), string, int> bigramMatrix, ICoOccurrenceMatrix<(string,string,string), string, int> trigramMatrix)
        {
            List<string> tokensCopy = new List<string>(tokens);

            string prev = START_GRAM, prev2 = START_GRAM;
            bool prevSpace = false;
            tokensCopy.Add(END_GRAM); // TODO: escape the brackets in any other tokens
            for (int i = 0; i < tokensCopy.Count; i++)
            {
                if (tokensCopy[i] == " ")
                {
                    prevSpace = true;
                }
                else
                {
                    unigramMatrix[tokensCopy[i], column]++;
                    if (prevSpace && includeSpaceUnigram)
                        unigramMatrix[tokensCopy[i], "space_before"]++;

                    if (prev != null)
                    {
                        bigramMatrix[(prev, tokensCopy[i]), column]++;
                    }

                    if (prev2 != null)
                    {
                        trigramMatrix[(prev2, prev, tokensCopy[i]), column]++;
                    }

                    prev2 = prev;
                    prev = tokensCopy[i];
                    prevSpace = false;
                }
            }
        }

        // TODO: maybe find better place for htis
        public static string ReplaceVariables(string text, Random r, List<string> usernames = null, List<string> links = null, List<string> overrideNumbers = null)
        {
            if (text == null)
                return text;

            { // replace links
                int index;
                while ((index = text.IndexOf(LINK_GRAM)) >= 0)
                {
                    string link = links != null && links.Count > 0 ? links[r.Next(links.Count)] : "http://www.schessgame.com/";
                    text = text.Substring(0, index) + link + text.Substring(index + LINK_GRAM.Length);
                }
            }

            { // replace numbers
                Regex findNumbers = new Regex(NUMBER_GRAM_PATTERN);
                MatchCollection matches = findNumbers.Matches(text);

                int offset = 0;
                foreach (Match m in matches)
                {
                    if (m.Success)
                    {
                        int numDigits = int.Parse(m.Groups["count"].Value);

                        string number = overrideNumbers != null && overrideNumbers.Count > 0 ? overrideNumbers[r.Next(overrideNumbers.Count)] : r.Next(numDigits * 10).ToString();
                        text = text.Substring(0, offset + m.Index) + number + text.Substring(offset + m.Index + m.Length);
                        offset += number.Length - m.Length;
                    }
                }
            }

            { // replace users
                int index;
                while ((index = text.IndexOf(USER_GRAM)) >= 0)
                {
                    string number = usernames.Count > 0 ? usernames[r.Next(usernames.Count)] : "my crush";
                    text = text.Substring(0, index) + number + text.Substring(index + USER_GRAM.Length);
                }
            }

            return text;
        }
    }
}
