using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class MarkovChain
{
    class MarkovState
    {
        public string gram;
        public int massCount;
        public SortedDictionary<string, int> next;

        public MarkovState(string gram)
        {
            this.gram = gram;
            this.massCount = 0;
            next = new SortedDictionary<string, int>();
        }
    }

    MarkovState START_GRAM = new MarkovState("THIS_IS_THE_STARTING_GRAM_BOOP_BEEP_BOOP1349857");
    MarkovState END_GRAM = new MarkovState("THIS_IS_THE_ENDING_GRAM_BOOP_BEEP_BOOP2462256");

    Dictionary<string, MarkovState> states = new Dictionary<string, MarkovState>();
    int order = -1;

    public MarkovChain()
    {
        states.Add(START_GRAM.gram, START_GRAM);
    }

    public void ClearData()
    {
        states.Clear();
        order = -1;
        states.Add(START_GRAM.gram, START_GRAM);
    }

    public bool LoadFromSave(string filepath)
    {
        if (!File.Exists(filepath))
            throw new FileNotFoundException("[MarkovChain] File Does Not Exist: " + filepath, filepath);

        string json;
        using (StreamReader reader = new StreamReader(filepath))
        {
            json = reader.ReadToEnd();
        }

        return LoadFromJson(json);
    }

    public bool LoadFromJson(string json)
    {
        JObject root = JObject.Parse(json);

        foreach (JProperty entry in root.Properties())
        {
            MarkovState state;
            if (!states.TryGetValue(entry.Name, out state))
                state = states[entry.Name] = new MarkovState(entry.Name);

            JObject rhs = entry.Value as JObject;

            state.massCount = (int)rhs.Property("count").Value;
            int checkCount = 0;

            JObject jNext = rhs.Property("next").Value as JObject;
            foreach (JProperty next in jNext.Properties())
            {
                state.next[next.Name] = (int)next.Value;
                checkCount += (int)next.Value;
            }

            if (checkCount != state.massCount)
                throw new System.Exception("Number Mismatch: " + checkCount + " != " + state.massCount);
        }

        return true;
    }

    public void Save(string filepath)
    {
        using (StreamWriter sw = new StreamWriter(filepath))
        using (JsonWriter writer = new JsonTextWriter(sw))
        {
            writer.WriteStartObject();
            foreach(KeyValuePair<string, MarkovState> kvp in states)
            {
                writer.WritePropertyName(kvp.Key);
                writer.WriteStartObject();
                {
                    writer.WritePropertyName("count");
                    writer.WriteValue(kvp.Value.massCount);
                    writer.WritePropertyName("next");
                    writer.WriteStartObject();
                    foreach (KeyValuePair<string, int> innerKvp in kvp.Value.next)
                    {
                        writer.WritePropertyName(innerKvp.Key);
                        writer.WriteValue(innerKvp.Value);
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
    }

    public void LoadGramsDelimeter(string text, string delimiter)
    {
        List<string> grams = new List<string>();
        if (string.IsNullOrEmpty(delimiter))
            grams.Add(text);
        else
            foreach (string s in text.Split(delimiter.ToCharArray(), System.StringSplitOptions.RemoveEmptyEntries))
                grams.Add(s);

        LoadGrams(grams);
    }

    public void LoadNGrams(string text, int order)
    {
        this.order = order;
        List<string> grams = new List<string>();

        for (int i = 0; i < text.Length; i += order)
        {
            grams.Add(text.Substring(i, Math.Min(order, text.Length - i)));
        }

        LoadGrams(grams);
    }

    public void LoadGrams(List<string> grams)
    {
        MarkovState prev = START_GRAM;

        foreach (string s in grams)
        {
            MarkovState curr;
            if (!states.TryGetValue(s, out curr))
                curr = states[s] = new MarkovState(s);

            prev.massCount++;
            if (prev.next.ContainsKey(s))
                prev.next[s]++;
            else
                prev.next[s] = 1;

            prev = curr;
        }

        prev.massCount++;
        if (prev.next.ContainsKey(END_GRAM.gram))
            prev.next[END_GRAM.gram]++;
        else
            prev.next[END_GRAM.gram] = 1;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="prevGram"></param>
    /// <param name="random">used for choosing the next gram</param>
    /// <param name="weightedRandom"></param>
    /// <param name="tryToComplete"></param>
    /// <returns></returns>
    public string GetNextGram(string prevGram, System.Random random, bool weightedRandom = true, bool tryToComplete = false, int tryTimes = 4)
    {
        MarkovState prev;
        if (!states.TryGetValue(prevGram, out prev))
            return "";

        string nextGram = "";

        int completeTries = 0;
        bool satisfied = false;
        while (!satisfied)
        {
            if (weightedRandom)
            {
                int randomChoice = random.Next(0, prev.massCount);
                int countIncrement = 0;
                foreach (KeyValuePair<string, int> kvp in prev.next)
                {
                    if (randomChoice >= countIncrement && randomChoice <= countIncrement + kvp.Value)
                    {
                        nextGram = kvp.Key;
                        break;
                    }

                    countIncrement += kvp.Value;
                }
            }
            else
            {
                int randomIndex = random.Next(0, prev.next.Count);
                foreach (string s in prev.next.Keys)
                {
                    if (randomIndex == 0)
                    {
                        nextGram = s;
                        break;
                    }
                    randomIndex--;
                }
            }

            if (!tryToComplete)
            {
                satisfied = true;
            }
            else
            {
                completeTries++;
                if (nextGram == END_GRAM.gram || states[nextGram].next.ContainsKey(END_GRAM.gram))
                    satisfied = true;
                else
                    satisfied = completeTries >= tryTimes;
            }
        }

        return nextGram;
    }

    public string GenerateSequence(System.Random random, int preferredLength, bool insertSpace = false, int maxLength = 1000, bool weightedRandom = true)
    {
        string generatedText = "";

        string gram = START_GRAM.gram;

        bool satisfied = false;
        while (!satisfied)
        {
            gram = GetNextGram(gram, random, weightedRandom, generatedText.Length + (order == -1 ? 0 : order * 3) >= preferredLength);

            if (gram != END_GRAM.gram)
                generatedText += gram;

            if (insertSpace)
                generatedText += " ";

            if (gram == "" || gram == END_GRAM.gram || generatedText.Length > maxLength)
                satisfied = true;
        }

        return generatedText;
    }
}
