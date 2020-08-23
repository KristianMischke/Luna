using System.Collections.Generic;
using System.IO;
using System;
using System.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Luna.Sentiment
{
    public class WordEmotion : IEnumerable<KeyValuePair<string, float>>
    {
        private Dictionary<string, float> emotionVector = new Dictionary<string, float>();
        public string word;

        public WordEmotion()
        {
            word = null;
        }
        public WordEmotion(string word)
        {
            this.word = word;
        }

        public void WriteToJson(JsonWriter writer)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("word");
            writer.WriteValue(word);

            writer.WritePropertyName("emotionVector");
            writer.WriteStartObject();
            foreach (KeyValuePair<string, float> kvp in emotionVector)
            {
                writer.WritePropertyName(kvp.Key);
                writer.WriteValue(kvp.Value);
            }
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        public void LoadJson(JObject obj)
        {
            if (obj == null)
                return;
            word = (string)obj["word"];
            foreach (JProperty prop in obj["emotionVector"].Children())
            {
                emotionVector.Add(prop.Name, (float)prop.Value);
            }
        }

        public WordEmotion Copy()
        {
            WordEmotion copy = new WordEmotion(word);
            foreach (KeyValuePair<string, float> kvp in emotionVector)
            {
                copy[kvp.Key] = kvp.Value;
            }
            return copy;
        }

        public float this[string emotion]
        {
            get => emotionVector.GetValueOrDefault(emotion, 0);
            set => emotionVector[emotion] = value;
        }

        public int CountDimensions { get => emotionVector.Count; }

        public WordEmotion Add(WordEmotion other)
        {
            foreach(KeyValuePair<string, float> kvp in other.emotionVector)
            {
                if (emotionVector.ContainsKey(kvp.Key))
                {
                    emotionVector[kvp.Key] += kvp.Value;
                }
                else
                {
                    emotionVector[kvp.Key] = kvp.Value;
                }
            }

            return this;
        }

        public WordEmotion Divide(float value)
        {
            List<string> keys = new List<string>(emotionVector.Keys);
            foreach (string key in keys)
            {
                emotionVector[key] /= value;
            }
            return this;
        }

        public WordEmotion Mult(float value)
        {
            List<string> keys = new List<string>(emotionVector.Keys);
            foreach (string key in keys)
            {
                emotionVector[key] *= value;
            }
            return this;
        }

        public (string emotion, float value) Max()
        {
            (string emotion, float value) max = (null, 0);
            foreach (KeyValuePair<string, float> kvp in emotionVector)
            {
                if(kvp.Value > max.value)
                {
                    max = (kvp.Key, kvp.Value);
                }
            }

            return max;
        }

        public WordEmotion CopyMax(WordEmotion other)
        {
            (string emotion, float value) max = other.Max();
            if (max.value > this[max.emotion])
            {
                emotionVector[max.emotion] = max.value;
            }
            return this;
        }

        public static async void LoadAsync(string path, Dictionary<string, WordEmotion> dict)
        {
            if (File.Exists(path))
            {
                using (StreamReader sr = new StreamReader(path))
                {
                    int i = 0;
                    string line;
                    string[] columns = null;
                    do
                    {
                        line = await sr.ReadLineAsync();
                        string[] entry = line?.Split(',');
                        if (i == 0)
                        {
                            columns = entry;
                        }
                        else if (entry != null && columns != null && entry.Length == columns.Length)
                        {
                            string word = entry[0].Trim();
                            WordEmotion newWordEmotion = new WordEmotion(word);

                            for (int j = 1; j < entry.Length; j++)
                            {
                                newWordEmotion[columns[j]] = float.Parse(entry[j]);
                            }
                            dict.Add(word, newWordEmotion);
                        }
                        i++;
                    } while (line != null);
                }
            }
        }

        public IEnumerator<KeyValuePair<string, float>> GetEnumerator() => emotionVector.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    //---------------
    public enum HedonometerColumn
    {
        WORD = 1,
        HAPPINESS = 3,
        STANDARD_DEVIATION = 4,

        COUNT = 5
    }
    public struct HedonometerEntry
    {
        public string word;
        public float happiness, sd;

        public static async void LoadAsync(string path, Dictionary<string, HedonometerEntry> dict)
        {
            if (File.Exists(path))
            {
                using (StreamReader sr = new StreamReader(path))
                {
                    string line;
                    int i = 0;
                    do
                    {
                        line = await sr.ReadLineAsync();
                        string[] entry = line?.Split(',');
                        if (i > 0 && entry != null && entry.Length == (int)HedonometerColumn.COUNT)
                        {
                            string word = entry[(int)HedonometerColumn.WORD].Trim('\"');

                            dict.Add(word, new HedonometerEntry
                            {
                                word = word,
                                happiness = float.Parse(entry[(int)HedonometerColumn.HAPPINESS].Trim('\"')),
                                sd = float.Parse(entry[(int)HedonometerColumn.STANDARD_DEVIATION].Trim('\"'))
                            });
                        }
                        i++;
                    } while (line != null);
                }
            }
        }

        public void WriteToJson(JsonWriter writer)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("word");
            writer.WriteValue(word);

            writer.WritePropertyName("happiness");
            writer.WriteValue(happiness);

            writer.WritePropertyName("sd");
            writer.WriteValue(sd);

            writer.WriteEndObject();
        }

        public void LoadJson(JObject obj)
        {
            if (obj == null)
                return;

            word = (string)obj["word"];
            happiness = (float)obj["happiness"];
            sd = (float)obj["sd"];
        }
    }

    public class MoodProfile
    {
        public WordEmotion emotion;
        public HedonometerEntry hedonometer;
        public float nukeyness;

        public MoodProfile()
        {
            emotion = new WordEmotion();
        }

        public MoodProfile(MoodProfile other)
        {
            emotion = other.emotion.Copy();
            hedonometer = other.hedonometer;
            nukeyness = other.nukeyness;
        }

        public void WriteToJson(JsonWriter writer)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("emotion");
            emotion.WriteToJson(writer);

            writer.WritePropertyName("hedonometer");
            hedonometer.WriteToJson(writer);

            writer.WritePropertyName("nukeyness");
            writer.WriteValue(nukeyness);

            writer.WriteEndObject();
        }

        public void LoadJson(JObject obj)
        {
            if (obj == null)
                return;
            emotion.LoadJson(obj["emotion"] as JObject);
            hedonometer.LoadJson(obj["hedonometer"] as JObject);
            nukeyness = (float)obj["nukeyness"];
        }

        public void Mix(MoodProfile other, float weight)
        {
            emotion.Mult(1f - weight).Add(other.emotion.Copy().Mult(weight));
            hedonometer = new HedonometerEntry() { happiness = hedonometer.happiness * (1f - weight) + other.hedonometer.happiness * weight };
            nukeyness = nukeyness * (1f - weight) + other.nukeyness * weight;
        }

        public MoodProfile Opposite(bool ignoreNukeyness)
        {
            MoodProfile oppositeMood = new MoodProfile();

            foreach (KeyValuePair<string, float> kvp in emotion)
            {
                oppositeMood.emotion[kvp.Key] = 1f - kvp.Value;
            }

            oppositeMood.hedonometer.happiness = 10 - hedonometer.happiness;
            if(!ignoreNukeyness)
                oppositeMood.nukeyness = 10 - nukeyness;
            return oppositeMood;
        }

        public string GetPrimaryMood()
        {
            if (nukeyness > 8)
            {
                return "nukey";
            }

            var max = emotion?.Max() ?? (null, 0);
            if (max.value > 0.06f)
            {
                if (max.emotion != "worry" || hedonometer.happiness < 5) //only use worry if less than 5 hedonometer happiness
                {
                    return max.emotion;
                }
            }

            if (hedonometer.happiness > 6)
            {
                return "happy";
            }
            if (hedonometer.happiness < 4)
            {
                return "sad";
            }

            return "neutral";
        }

        public override string ToString()
        {
            string primaryMood = GetPrimaryMood();
            string result = $"---{primaryMood}---\n";
            foreach (KeyValuePair<string, float> kvp in emotion)
            {
                result += $"{kvp.Key}: {kvp.Value}, ";
            }
            result += $"\nhedonometer: {hedonometer.happiness}" +
                $"\nnukeyness: {nukeyness}" +
                $"\n---{String.Join("-", new string[primaryMood.Length])}---";
            return result;
        }
    }

    public static class MoodMarkovExtension
    {
        public static string GenerateSequence(this MarkovChain markov, MoodProfile moodGoal, Random random, int preferredLength, Func<string, MoodProfile> getMood, bool insertSpace = false, int maxLength = 1000, bool weightedRandom = true)
        {
            string generatedText = "";
            string tempGram = "";
            string gram = markov.StartGram;

            string targetMood = moodGoal.GetPrimaryMood();

            bool satisfied = false;
            while (!satisfied)
            {
                // try 4 times to get the mood of the text to match the goal before moving to the next gram
                //for (int i = 0; i < 10; i++)
                {
                    tempGram = markov.GetNextGram(gram, random, (x) => GetBestEmotion(x, moodGoal, random, getMood), generatedText.Length + (markov.Order == -1 ? 0 : markov.Order * 3) >= preferredLength);

                    /*MoodProfile testMood = getMood(generatedText + gram);
                    if (testMood.GetPrimaryMood() == targetMood)
                    {
                        break;
                    }*/
                }
                gram = tempGram;

                if (gram != markov.EndGram)
                    generatedText += gram;

                if (insertSpace)
                    generatedText += " ";

                if (gram == "" || gram == markov.EndGram || generatedText.Length > maxLength)
                    satisfied = true;
            }

            return generatedText;
        }

        private static string GetBestEmotion(MarkovChain.MarkovState prev, MoodProfile moodGoal, Random r, Func<string, MoodProfile> getMood)
        {
            string moodGoalPrimary = moodGoal.GetPrimaryMood();

            SortedList<double, string> sorted = new SortedList<double, string>();

            foreach (KeyValuePair<string, int> kvp in prev.next)
            {
                MoodProfile m = getMood(kvp.Key);

                if (m.GetPrimaryMood() == moodGoalPrimary)
                {
                    sorted[r.NextDouble()] = kvp.Key; // very low number for target mood (i.e. top of the list)
                }
                else if (moodGoalPrimary == "nukey")
                {
                    sorted[10 - m.nukeyness + r.NextDouble()] = kvp.Key;
                }
                else
                {
                    sorted[10 - m.emotion[moodGoalPrimary] + r.NextDouble()] = kvp.Key;
                }
            }

            // get a random item near the front of the list
            int index = 0;
            for(int i = 0; i < 10; i++)
            {
                if (r.NextDouble() < 0.3 && index < sorted.Count-2)
                {
                    index++;
                }
            }

            if (sorted.Values.Count == 0)
            {
                Console.WriteLine("[WordSentiment] Values.Count == 0");
                Console.WriteLine($"prev = {prev.gram}");
            }

            return sorted.Values[index];
        }
    }
}