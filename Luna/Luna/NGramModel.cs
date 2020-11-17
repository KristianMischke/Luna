using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Luna
{
    public class NGramModel
    {
        private class NGramEntry
        {
            public string[] grams;
            public int count;

            public NGramEntry(string[] grams, int count)
            {
                this.grams = grams;
                this.count = count;
            }
        }

        private const string START_GRAM = "<START_GRAM>";
        private const string END_GRAM = "<END_GRAM>";
        private readonly byte n;

        private Dictionary<string, Dictionary<string, NGramEntry>> allEntries = new Dictionary<string, Dictionary<string, NGramEntry>>();
        private Dictionary<string, int> unigramCounts = new Dictionary<string, int>();

        public byte N => n;

        public NGramModel(byte n)
        {
            this.n = n;
        }

        public void AddGrams(List<string> grams)
        {
            // pad the grams with start and end grams
            for (int i = 0; i < n-1; i++)
            {
                grams.Insert(0, START_GRAM);
            }
            grams.Add(END_GRAM);

            for (int i = n; i < grams.Count; i++)
            {
                // get or create entries for this gram
                if (!allEntries.TryGetValue(grams[i], out Dictionary<string, NGramEntry> gramEntries))
                {
                    unigramCounts[grams[i]] = 1;
                    allEntries[grams[i]] = gramEntries = new Dictionary<string, NGramEntry>();
                }

                unigramCounts[grams[i]]++; // update unigram count

                string combinedNGram = string.Join(string.Empty, grams, i - n, n);

                // get or create n-gram entry
                if (!gramEntries.TryGetValue(combinedNGram, out NGramEntry entry))
                {
                    string[] ngramStrings = new string[n];
                    for (int j = 0; j < n; j++) ngramStrings[j] = grams[i - n + j];
                    gramEntries[combinedNGram] = entry = new NGramEntry(ngramStrings, 1);
                }

                entry.count++; // update n-gram entry count
            }
        }
    }
}
