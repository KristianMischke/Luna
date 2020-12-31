
using System;
using System.Collections.Generic;
using System.Text;

namespace LingK
{
    public interface ILanguageModel
    {
        float NaiveLogProb(List<string> words, int laplaceSmoothing = 0, bool padSentence = true);
    }

    public class UnigramModel : NGramModel<string, string>
    {
        public override int N => 1;

        public UnigramModel(int oovOccurrenceThreshold = 0)
        {
            ngramCounts = unigramMap = new CoOccurrenceDict<string, int>();
            historyCounts = new CoOccurrenceDict<string, int>();
            Init(oovOccurrenceThreshold);
        }
        public UnigramModel(ICoOccurrenceColumn<string, string, int> unigramMap, int oovOccurrenceThreshold = 0)
        {
            this.ngramCounts = this.unigramMap = unigramMap;
            historyCounts = new CoOccurrenceDict<string, int>();
            Init(oovOccurrenceThreshold);
        }
        protected override string GetNGramTuple(List<string> words, int start) => words[start];
        protected override string GetHistoryTuple(List<string> words, int start) => "";
        protected override string GetHistoryTuple(string ngram) => "";
        protected override string GetLastGram(string ngram) => ngram;
        protected override string CombineHistory(string history, string token) => token;
        protected override string ReplaceOOV(string ngram) => ReplaceOOVWord(ngram);

        protected override int Normalizer(string x, int laplaceSmoothing = 0) => unigramMap.Sum() + unigramMap.Count * laplaceSmoothing;
    }

    public class BigramModel : NGramModel<(string,string), string>
    {
        public override int N => 2;

        public BigramModel(int oovOccurrenceThreshold = 0)
        {
            unigramMap = new CoOccurrenceDict<string, int>();
            historyCounts = new CoOccurrenceDict<string, int>();
            ngramCounts = new CoOccurrenceDict<(string, string), int>();
            Init(oovOccurrenceThreshold);
        }
        public BigramModel(ICoOccurrenceColumn<string, string, int> unigramMap, ICoOccurrenceColumn<(string, string), string, int> bigramMap, int oovOccurrenceThreshold = 0)
        {
            this.unigramMap = unigramMap;
            historyCounts = unigramMap;
            ngramCounts = bigramMap;
            Init(oovOccurrenceThreshold);
        }

        protected override (string, string) GetNGramTuple(List<string> words, int start) => (words[start], words[start+1]);
        protected override string GetHistoryTuple(List<string> words, int start) => words[start];
        protected override string GetHistoryTuple((string, string) ngram) => ngram.Item1;
        protected override string GetLastGram((string, string) ngram) => ngram.Item2;
        protected override (string, string) CombineHistory(string history, string token) => (history, token);
        protected override (string, string) ReplaceOOV((string, string) ngram) => (ReplaceOOVWord(ngram.Item1), ReplaceOOVWord(ngram.Item2));

        protected override int Normalizer(string x, int laplaceSmoothing = 0)
        {
            int historyCount;
            if (x == BOS)
            {
                historyCount = NumSentences();
            }
            else
            {
                historyCount = historyCounts[x];
            }

            return historyCount + NumTokens() * laplaceSmoothing;
        }
    }

    public class TrigramModel : NGramModel<(string, string, string), (string, string)>
    {
        public override int N => 3;

        public TrigramModel(int oovOccurrenceThreshold = 0)
        {
            unigramMap = new CoOccurrenceDict<string, int>();
            historyCounts = new CoOccurrenceDict<(string, string), int>();
            ngramCounts = new CoOccurrenceDict<(string, string, string), int>();
            Init(oovOccurrenceThreshold);
        }
        public TrigramModel(ICoOccurrenceColumn<string, string, int> unigramMap,
                            ICoOccurrenceColumn<(string, string), string, int> bigramMap,
                            ICoOccurrenceColumn<(string, string, string), string, int> trigramMap, int oovOccurrenceThreshold = 0)
        {
            this.unigramMap = unigramMap;
            historyCounts = bigramMap;
            ngramCounts = trigramMap;
            Init(oovOccurrenceThreshold);
        }

        protected override (string, string, string) GetNGramTuple(List<string> words, int start) => (words[start], words[start + 1], words[start + 2]);
        protected override (string, string) GetHistoryTuple(List<string> words, int start) => (words[start], words[start + 1]);
        protected override (string, string) GetHistoryTuple((string, string, string) ngram) => (ngram.Item1, ngram.Item2);
        protected override string GetLastGram((string, string, string) ngram) => ngram.Item3;
        protected override (string, string, string) CombineHistory((string, string) history, string token) => (history.Item1, history.Item2, token);
        protected override (string, string, string) ReplaceOOV((string, string, string) ngram) => (ReplaceOOVWord(ngram.Item1), ReplaceOOVWord(ngram.Item2), ReplaceOOVWord(ngram.Item3));

        protected override int Normalizer((string, string) x, int laplaceSmoothing = 0)
        {
            int historyCount;
            if (x.Item1 == BOS && x.Item2 == BOS)
            {
                historyCount = NumSentences();
            }
            else
            {
                historyCount = historyCounts[x];
            }

            return historyCount + NumTokens() * laplaceSmoothing;
        }
    }

    public abstract class NGramModel<TNGramTuple, THistoryTuple> : ILanguageModel
    {
        private struct CacheTypeCountScope : IDisposable
        {
            NGramModel<TNGramTuple, THistoryTuple> nGramModel;
            bool prevState;
            public CacheTypeCountScope(NGramModel<TNGramTuple, THistoryTuple> nGramModel, bool useCached)
            {
                this.nGramModel = nGramModel;
                prevState = nGramModel.useCachedTokenCount;
                this.nGramModel.useCachedTokenCount = useCached;
            }

            public void Dispose()
            {
                this.nGramModel.useCachedTokenCount = prevState;
            }
        }

        // sentinel tokens
        public const string BOS = BasicTokenizer.START_GRAM;
        public const string EOS = BasicTokenizer.END_GRAM;
        public const string OOV = "<OOV>";

        // model parameters
        private int oovOccurrenceThreshold;

        // counts & count Dictionaries
        protected ICoOccurrenceColumn<string, string, int> unigramMap;             // OOV-replaced unigram map
        protected ICoOccurrenceColumn<TNGramTuple, string, int> ngramCounts;       // count of N-grams map
        protected ICoOccurrenceColumn<THistoryTuple, string, int> historyCounts;   // count of (N-1)-grams history map

        public abstract int N { get; }
        public int NumSentences() { return unigramMap[EOS]; }
        public int NumTypes() { return unigramMap.Count; }

        int cachedTokenCount = -1;
        public int NumTokens()
        {
            if (cachedTokenCount == -1 || !(useCachedTokenCount || modelFrozen))
            {
                cachedTokenCount = unigramMap.SumIf(x => x.value > oovOccurrenceThreshold || x.rowKey == OOV); //TODO: OOV count, numtokens and numtypes aren't working properly with OOV
            }
            return cachedTokenCount;
        }
        private bool useCachedTokenCount = false;
        private bool modelFrozen = false;

        // Given a filepath, deserializes to an NGramModel object and returns the result or null if it failed
        //public static NGramModel<NTuple, HistoryTuple> FromFile(string filepath)
        //{
        //    try
        //    {
        //        FileInputStream file = new FileInputStream(filepath);
        //        ObjectInputStream in = new ObjectInputStream(file);
        //
        //        NGramModel obj = (NGramModel)in.readObject();
        //
        //            in.close();
        //        file.close();
        //        return obj;
        //    }
        //    catch (IOException | ClassNotFoundException e) {
        //        e.printStackTrace();
        //    }
        //    return null;
        //}

        protected void Init(int oovOccurrenceThreshold)
        {
            this.oovOccurrenceThreshold = oovOccurrenceThreshold;
        }

        public void FreezeModel()
        {
            modelFrozen = true;
        }

        private void AddSentence(List<string> sentence, bool padSentence = true)
        {
            if (modelFrozen)
                return; // TODO: warning msg

            ReplaceOOV(sentence);
            if(padSentence)
                PadSentence(sentence);

            foreach (string token in sentence) unigramMap[token]++;

            for (int i = 0; i < sentence.Count - N + 1; i++)
            {
                TNGramTuple key = GetNGramTuple(sentence, i);
                THistoryTuple historyKey = GetHistoryTuple(sentence, i);

                // increment N-gram counts
                if (unigramMap != ngramCounts) // special-case to avoid duplicate entries for unigram models
                    ngramCounts[key]++;

                // increment N-1 gram counts for the history/normalizer
                if (unigramMap != ngramCounts) // special-case to avoid duplicate entries for bigram models
                    historyCounts[historyKey]++;
            }
        }

        private void PadSentence(List<string> sentence)
        {
            // pad the sentence with (N-1) Beginning Of Sentence tokens
            for (int i = 0; i < N - 1; i++)
            {
                sentence.Insert(0, BOS);
            }
            sentence.Add(EOS); // pad with End Of Sentence token
        }


        // replaces OOV words in-place
        protected void ReplaceOOV(List<string> words)
        {
            for (int i = 0; i < words.Count; i++)
            {
                words[i] = ReplaceOOVWord(words[i]);
            }
        }

        protected abstract TNGramTuple ReplaceOOV(TNGramTuple ngram);
        protected string ReplaceOOVWord(string word)
        {
            // apply Out Of Vocabulary if we have a map of the unigram counts and is under the threshold
            if (unigramMap.TryGetValue(word, out int value) && value <= oovOccurrenceThreshold)
            {
                return OOV;
            }
            return word;
        }

        protected abstract TNGramTuple GetNGramTuple(List<string> words, int start);
        protected abstract THistoryTuple GetHistoryTuple(List<string> words, int start);
        protected abstract THistoryTuple GetHistoryTuple(TNGramTuple ngram);
        protected abstract string GetLastGram(TNGramTuple ngram);
        protected abstract TNGramTuple CombineHistory(THistoryTuple history, string token);


        // assumes model has been trained, returns count of N-gram
        public int C(TNGramTuple x, int laplaceSmoothing = 0) => ngramCounts[x] + laplaceSmoothing;

        // assumes model has been trained
        protected abstract int Normalizer(THistoryTuple x, int laplaceSmoothing = 0);

        // assumes model has been trained
        public float P(TNGramTuple joint, int laplaceSmoothing = 0)
        {
            TNGramTuple replacedJoint = ReplaceOOV(joint);
            THistoryTuple history = GetHistoryTuple(replacedJoint);

            return ((float)C(replacedJoint, laplaceSmoothing)) / Normalizer(history, laplaceSmoothing);
        }

        // assumes model has been trained
        public float NaiveLogProb(List<string> sentence, int laplaceSmoothing = 0, bool padSentence = true)
        {
            List<string> copy = new List<string>(sentence);
            ReplaceOOV(copy);
            if (padSentence)
                PadSentence(copy);

            float total = 0;
            using (new CacheTypeCountScope(this, true))
            {
                for (int i = 0; i < copy.Count - N + 1; i++)
                {
                    TNGramTuple ngram = GetNGramTuple(copy, i);
                    total += (float)Math.Log(P(ngram, laplaceSmoothing));
                }
            }

            return total;
        }

        // assumes model has been trained
        public float Perplexity(IEnumerable<IEnumerable<string>> corpus, bool padSentences = true)
        {
            Dictionary<TNGramTuple, int> corpusCounts = new Dictionary<TNGramTuple, int>();
            int totalCounts = 0;
            foreach (IEnumerable<string> sentence in corpus)
            {
                List<string> copy = new List<string>(sentence);
                ReplaceOOV(copy);
                if (padSentences)
                    PadSentence(copy);
                for (int i = 0; i < copy.Count - N; i++)
                {
                    TNGramTuple key = GetNGramTuple(copy, i);

                    // increment N-gram counts
                    if (corpusCounts.TryGetValue(key, out int prevValue))
                    {
                        corpusCounts[key] = prevValue + 1;
                    }
                    else
                    {
                        corpusCounts[key] = 1;
                    }

                    totalCounts++;
                }
            }

            float perplexity = 0;
            using (new CacheTypeCountScope(this, true))
            {
                foreach (var kvp in corpusCounts)
                {
                    perplexity += kvp.Value * (float)Math.Log(P(kvp.Key));
                }
            }

            perplexity *= -1f / totalCounts;
            perplexity = (float)Math.Exp(perplexity);

            return perplexity;
        }

        public void ProbabilityTest()
        {
            using (new CacheTypeCountScope(this, true))
            {
                bool failed = false;
                foreach (var kvp in historyCounts)
                {
                    THistoryTuple history = kvp.Key;

                    float totalProb = 0f;
                    foreach (string y in unigramMap.GetRowKeys())
                    {
                        totalProb += P(CombineHistory(history, y));
                    }

                    if (totalProb < 0.99f || totalProb > 1.01)
                    {
                        Console.WriteLine(kvp.Key + " : " + kvp.Value + " totalProb: " + totalProb);
                        failed = true;
                    }
                }

                if (!failed)
                {
                    Console.WriteLine("Passed Probability Tests");
                }
            }
        }

    }
}