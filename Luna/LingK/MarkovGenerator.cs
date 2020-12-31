using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace LingK
{
    public class MarkovGenerator
    {

        // https://en.wikipedia.org/wiki/Katz%27s_back-off_model





        public static float StupidBackoff(List<string> tokens,
            int i,
            UnigramModel unigramModel,
            BigramModel bigramModel,
            TrigramModel trigramModel
            )
        {
            if (i < 2)
            {
                Console.WriteLine("[MarkovGenerator] i must be >= 2");
                return 0;
            }

            string x = tokens[i - 2];
            string y = tokens[i - 1];
            string z = tokens[i];

            (string, string, string) trigram = (x, y, z);
            if (trigramModel.C(trigram) > 0)
            {
                return trigramModel.P(trigram);
            }

            (string, string) bigram = (y, z);
            if (bigramModel.C(bigram) > 0)
            {
                return 0.4f * bigramModel.P(bigram);
            }

            return 0;// 0.4f * 0.4f * unigramModel.P(z); // Unigram in stupid backoff doesn't seem to generate great results
        }

        public static float TrigramP(List<string> tokens,
            int i,
            UnigramModel unigramModel,
            BigramModel bigramModel,
            TrigramModel trigramModel
            )
        {
            if (i < 2)
            {
                Console.WriteLine("[MarkovGenerator] i must be >= 2");
                return 0;
            }

            string x = tokens[i - 2];
            string y = tokens[i - 1];
            string z = tokens[i];

            (string, string, string) trigram = (x, y, z);
            return trigramModel.P(trigram);
        }

        public delegate float BackoffFunction(List<string> tokens, int i, UnigramModel unigramModel, BigramModel bigramModel, TrigramModel trigramModel);

        static Regex spaceBeforePunctuation = new Regex(@"^[`#*(+={[]+$");
        static Regex spaceAfterPunctuation = new Regex(@"^[.,<>?!~%)+=}\]:;]+$");
        static Regex punctuation = new Regex(@"^[~!@#$%^&*()_+=\-`{}[\]|\\;':""<>?,.\/]+$");
        public static string GenerateWithBackoff(
            Random random,
            UnigramMatrix<int> unigramMatrix,
            BigramMatrix<int> bigramMatrix,
            TrigramMatrix<int> trigramMatrix,
            BackoffFunction backoffFunction
            )
        {
            Dictionary<string, float> probabilities = new Dictionary<string, float>();
            List<string> tokens = new List<string>();
            tokens.Add(BasicTokenizer.START_GRAM);
            tokens.Add(BasicTokenizer.START_GRAM);

            UnigramModel unigramModel = new UnigramModel(unigramMatrix.GetColumn("all"));
            BigramModel bigramModel = new BigramModel(unigramMatrix.GetColumn("all"), bigramMatrix.GetColumn("all"));
            TrigramModel trigramModel = new TrigramModel(unigramMatrix.GetColumn("all"), bigramMatrix.GetColumn("all"), trigramMatrix.GetColumn("all"));
            unigramModel.FreezeModel();
            bigramModel.FreezeModel();
            trigramModel.FreezeModel();

            while (tokens[tokens.Count-1] != BasicTokenizer.END_GRAM && tokens[tokens.Count-1] != null)
            {
                // get token probabilities
                probabilities.Clear();
                float probabilitiesSum = 0f;
                foreach (var kvp in unigramMatrix.GetColumn("all"))
                {
                    tokens.Add(kvp.Key);

                    float backoffProb = backoffFunction(tokens, tokens.Count - 1, unigramModel, bigramModel, trigramModel);
                    if (!float.IsInfinity(backoffProb))
                    {
                        probabilities[kvp.Key] = backoffProb;
                        probabilitiesSum += backoffProb;
                    }

                    tokens.RemoveAt(tokens.Count - 1);
                }

                // pick a token
                float threshold = (float)random.NextDouble()*probabilitiesSum;
                string pickedToken = null;
                foreach (var kvp in probabilities.OrderByDescending(x => x.Value))
                {
                    threshold -= kvp.Value;
                    if (threshold <= 0)
                    {
                        pickedToken = kvp.Key;
                        break;
                    }
                }

                if (pickedToken == null)
                {
                    return "";
                }

                tokens.Add(pickedToken);
            }

            // stitch string together
            StringBuilder result = new StringBuilder();
            bool prevPunctuation = true;
            string prevToken = "";
            foreach (string token in tokens)
            {
                if (token != BasicTokenizer.START_GRAM && token != BasicTokenizer.END_GRAM)
                {
                    bool currentPunctuation = punctuation.IsMatch(token);

                    if ((!currentPunctuation && prevPunctuation && spaceAfterPunctuation.IsMatch(prevToken))
                        || (currentPunctuation && !prevPunctuation && spaceBeforePunctuation.IsMatch(token))
                        || (!currentPunctuation && !prevPunctuation))
                    {
                        result.Append(" ");
                    }

                    result.Append(token);
                    prevPunctuation = currentPunctuation;
                    prevToken = token;
                }
            }

            return result.ToString();
        }
    }
}
