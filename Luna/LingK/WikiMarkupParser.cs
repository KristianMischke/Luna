using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace LingK
{
    public class WikiMarkupParser
    {
        public class MarkupEntry
        {
            public string originalContent;

            // header info
            public bool isHeader;
            public string headerKey;
            public string headerValue;
        }

        List<MarkupEntry> markupEntries = new List<MarkupEntry>();
        List<MarkupEntry> contentEntries = new List<MarkupEntry>();
        Dictionary<string, MarkupEntry> headers = new Dictionary<string, MarkupEntry>();

        public WikiMarkupParser()
        { }

        public MarkupEntry GetHeader(string key)
        {
            if (headers.TryGetValue(key, out MarkupEntry result))
            {
                return result;
            }
            return null;
        }

        public MarkupEntry this[int index]
        {
            get => contentEntries[index];
        }

        public int ContentCount => contentEntries.Count;

        public void Load(string data)
        {
            markupEntries.Clear();
            contentEntries.Clear();
            headers.Clear();

            StringBuilder entryContent = new StringBuilder();
            int curlyBraceDepth = 0;
            MarkupEntry nextEntry = new MarkupEntry();

            int prevI = 0;
            int i = 0;
            while (i >= 0)
            {
                bool addEntry = false;

                i = data.IndexOf("\n", prevI);

                if (i == -1)
                {
                    i = data.Length;
                }

                string line = data.Substring(prevI, i - prevI);
                curlyBraceDepth += line.Length - line.Replace("{{", "{").Length;
                if (line.StartsWith("{{")) // begin markup entry
                {
                    if (entryContent.Length == 0)
                    {
                        nextEntry.isHeader = true;
                    }
                }

                if (!string.IsNullOrEmpty(line))
                {
                    entryContent.AppendLine(line);

                    curlyBraceDepth -= line.Length - line.Replace("}}", "}").Length;
                    if (line.EndsWith("}}") && curlyBraceDepth == 0)
                    {
                        string content = nextEntry.originalContent = entryContent.ToString();

                        int keyEndIndex = content.IndexOf('|');
                        keyEndIndex = keyEndIndex == -1 ? content.Length : keyEndIndex;
                        nextEntry.headerKey = content.Substring(2, keyEndIndex - 4);
                        nextEntry.headerValue = keyEndIndex == content.Length ? "" : content.Substring(keyEndIndex+1, (content.Length - 4) - (keyEndIndex+1));
                        headers.Add(nextEntry.headerKey, nextEntry);
                        addEntry = true;
                    }
                }
                else if (entryContent.Length > 0)// empty line, so end the entry
                {
                    nextEntry.originalContent = entryContent.ToString();
                    addEntry = true;
                }

                if (addEntry)
                {
                    entryContent.Clear();
                    markupEntries.Add(nextEntry);
                    if (!nextEntry.isHeader)
                    {
                        contentEntries.Add(nextEntry);
                    }

                    nextEntry = new MarkupEntry();
                }

                if (i == data.Length)
                {
                    break;
                }
                prevI = i+1;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="markupEntry"></param>
        /// <param name="hyperlinkSource">url root for hyperlinks (should end in slash)</param>
        /// <param name="hyperlinkPattern">markdown pattern for hyperlinks (must include {{LINK}} and {{TEXT}} tags for raplacement)</param>
        /// <param name="replaceBold"></param>
        /// <param name="replaceItalics"></param>
        /// <returns></returns>
        public static string Format(MarkupEntry markupEntry, string hyperlinkSource = null, string hyperlinkPattern = null, string replaceBold = "**", string replaceItalics = "*")
        {
            StringBuilder result = new StringBuilder(markupEntry.originalContent.Length);

            Regex matchRegex = new Regex(@"(?<reference><ref.*/>|<ref>.*</ref>)|(?<hyperlink>\[\[(?<linkTo>[^]]+)(\|(?<linkText>[^]]+))?\]\](?<extra>\w+)?)");

            int i = 0;
            while (i >= 0 && i < markupEntry.originalContent.Length)
            {
                Match m = matchRegex.Match(markupEntry.originalContent, i);

                if (m.Success)
                {
                    if (m.Index > i) // add content before the match
                    {
                        result.Append(markupEntry.originalContent.Substring(i, m.Index - i).Replace("'''", replaceBold).Replace("''", replaceItalics));
                    }

                    if (m.Groups["reference"].Success)
                    {
                        // skip references for now
                    }
                    else if (m.Groups["hyperlink"].Success)
                    {
                        string linkTo = m.Groups["linkTo"].Value;
                        string linkText = m.Groups["linkText"].Value;
                        string extra = m.Groups["extra"].Value;

                        if (string.IsNullOrWhiteSpace(linkText))
                        {
                            linkText = linkTo;
                        }

                        if (string.IsNullOrEmpty(hyperlinkSource) || string.IsNullOrEmpty(hyperlinkPattern))
                        {
                            result.Append(linkText).Append(extra);
                        }
                        else
                        {
                            result.Append(hyperlinkPattern.Replace("{{LINK}}", hyperlinkSource + linkTo.Replace(' ', '_')).Replace("{{TEXT}}", linkText + extra));
                        }
                    }

                    i = m.Index + m.Length;
                }
                else
                {
                    result.Append(markupEntry.originalContent.Substring(i, markupEntry.originalContent.Length - i));
                    i = markupEntry.originalContent.Length;
                }
            }

            return result.ToString();
        }
    }
}
