using System;
using System.Collections.Generic;
using System.Text;

namespace LingK
{
    /*
     * NOTE: Ported and adjusted from Python code by me (Kristian Mischke) for
     * CMSC 473 Intro to Natural Language Processing class at UMBC (University
     * of Maryland Baltimore County) in Fall 2020
     *
     * PMI = Pointwise Mutual Information
     */
    public class PMICalculator
    {
        protected ICoOccurrenceColumn<string, string> counts;
        protected ICoOccurrenceColumn<(string, string), string> pairCounts;

        protected int N => counts.Sum();
        protected int TotalPairCounts => pairCounts.Sum();

        public PMICalculator()
        {
            counts = new CoOccurrenceDict<string>();
            pairCounts = new CoOccurrenceDict<(string, string)>();
        }
        public PMICalculator(ICoOccurrenceColumn<string, string> counts, ICoOccurrenceColumn<(string, string), string> pairCounts)
        {
            this.counts = counts;
            this.pairCounts = pairCounts;
        }

        public void AddSentence(IEnumerable<string> sentence, bool updateUnigram)
        {
            List<string> prevWords = new List<string>();
            foreach (string word in sentence)
            {
                if (updateUnigram)
                {
                    if (counts.ContainsKey(word))
                    {
                        counts[word]++;
                    }
                    else
                    {
                        counts[word] = 1;
                    }
                }

                foreach (string prevWord in prevWords)
                {
                    (string, string) pair = (prevWord, word);
                    if (pairCounts.ContainsKey(pair))
                    {
                        pairCounts[pair]++;
                    }
                    else
                    {
                        pairCounts[pair] = 1;
                    }
                }
                prevWords.Add(word);
            }
        }

        public int C(string word)
        {
            if (counts.TryGetValue(word, out int c)) return c;
            return 0;
        }

        public float P(string v) => C(v) / (float)N; // marginal probability

        // joint probability
        public float P(string v, string n)
        {
            if (pairCounts.TryGetValue((v, n), out int count)) return count / (float) TotalPairCounts;
            return 0;
        }

        // since our joint probability is not an estimation, we don't need to subtract math.log(w-1, 2) like Church and Hank did
        public float PMI(string v, string n) => (float)Math.Log(P(v, n) / (P(v) * P(n)), 2);

        public float PLambda(string v, float lambda) => (C(v) + lambda) / (N + lambda*counts.Count); // marginal probability (lambda adjusted) counts.Count == num word types
        public float PLambda(string v, string n, float lambda)
        {
            if (pairCounts.TryGetValue((v, n), out int count)) return (count + lambda) / (TotalPairCounts + lambda*lambda*counts.Count*counts.Count);
            return 0;
        }
        public float PMILambda(string v, string n, float lambda) => (float)Math.Log(PLambda(v, n, lambda) / (PLambda(v, lambda)*PLambda(n, lambda)), 2);
    }
}
