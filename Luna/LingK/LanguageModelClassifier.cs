using System;
using System.Collections.Generic;
using System.Text;

namespace LingK
{
    public interface ILanguageModelClassifier
    {
        string Predict(List<string> words, int laplaceSmoothing = 0, bool padSentence = true);
    }

    public class NaiveBayesClassifier : ILanguageModelClassifier
    {
        private readonly Dictionary<string, ILanguageModel> languageModels = new Dictionary<string, LingK.ILanguageModel>();
        private readonly Dictionary<string, float> languagePriorProb = new Dictionary<string, float>();
        private readonly Dictionary<string, float> biasMap = new Dictionary<string, float>();

        public void AddLanguageModel(string languageName, ILanguageModel model, float priorProbability)
        {
            languageModels.Add(languageName, model);
            languagePriorProb[languageName] = priorProbability;
        }

        public void SetBias(string languageName, float value)
        {
            biasMap.Add(languageName, value);
        }
        public void ClearBias()
        {
            biasMap.Clear();
        }

        public string Predict(List<string> words, int laplaceSmoothing = 0, bool padSentence = true)
        {
            string bestModel = "";
            float bestValue = float.NegativeInfinity;

            foreach (var kvp in languageModels)
            {
                ILanguageModel model = kvp.Value;

                float bias = 0f;
                biasMap.TryGetValue(kvp.Key, out bias);
                float priorLog = (float)Math.Log(languagePriorProb[kvp.Key]);
                float predictedValue = model.NaiveLogProb(words, laplaceSmoothing, padSentence) + priorLog + bias;

                if (predictedValue > bestValue)
                {
                    bestModel = kvp.Key;
                    bestValue = predictedValue;
                }
            }

            return bestModel;
        }

        public string Baseline(List<string> words)
        {
            string bestModel = "";
            float bestValue = float.NegativeInfinity;

            foreach (var kvp in languageModels)
            {
                float predictedValue = (float)Math.Log(languagePriorProb[kvp.Key]); // baseline solely on prior probability

                if (predictedValue > bestValue)
                {
                    bestModel = kvp.Key;
                    bestValue = predictedValue;
                }
            }

            return bestModel;
        }
    }
}
