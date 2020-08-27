using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Luna.System;

public class MarkovChain
{
    public class MarkovState
    {
        public string gram;
        public int massCount;
        public SortedDictionary<string, int> next;
        public HashSet<string> prev;

        public MarkovState(string gram)
        {
            this.gram = gram;
            this.massCount = 0;
            next = new SortedDictionary<string, int>();
            prev = new HashSet<string>();
        }
    }

    MarkovState START_GRAM = new MarkovState("THIS_IS_THE_STARTING_GRAM_BOOP_BEEP_BOOP1349857");
    MarkovState END_GRAM = new MarkovState("THIS_IS_THE_ENDING_GRAM_BOOP_BEEP_BOOP2462256");
    public string StartGram => START_GRAM.gram;
    public string EndGram => END_GRAM.gram;

    Dictionary<string, MarkovState> states = new Dictionary<string, MarkovState>();
    int order = -1;
    public int Order => order;

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
                if (!states.TryGetValue(next.Name, out MarkovState nextState))
                    nextState = states[next.Name] = new MarkovState(next.Name);
                nextState.prev.Add(state.gram);

                state.next[next.Name] = (int)next.Value;
                checkCount += (int)next.Value;
            }

            if (checkCount != state.massCount)
            {
                Console.WriteLine($"Number Mismatch: {checkCount} != {state.massCount}\nUpdating total to {checkCount}");
                state.massCount = checkCount;
            }
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

    public void LoadGramsDelimiter(string text, string delimiter, int together = 1, int numShifts = 1)
    {
        if (numShifts <= 0)
        {
            Console.WriteLine("[MarkovChain] Num Shifts should be > 0");
            return;
        }

        List<string>[] gramsByShift = new List<string>[numShifts];
        for (int i = 0; i < numShifts; i++)
            gramsByShift[i] = new List<string>();

        if (string.IsNullOrEmpty(delimiter))
        {
            gramsByShift[0].Add(text);
        }
        else
        {
            string[] combined = new string[numShifts];
            for (int i = 0; i < numShifts; i++)
            {
                combined[i] = "";
            }

            string[] grams = text.Split(delimiter.ToCharArray(), System.StringSplitOptions.RemoveEmptyEntries);
            for(int i = 0; i < grams.Length; i++)
            {
                string s = grams[i];
                for (int j = 0; j < numShifts; j++)
                {
                    if ((i + j + 1) % together == 0) // [i] index of current word. [j] shift amount. [1] remove zero indexing for mod calculation
                    {
                        combined[j] += s;
                        gramsByShift[j].Add(combined[j]);
                        combined[j] = "";
                    }
                    else if (i == grams.Length-1)
                    {
                        combined[j] += s;
                        gramsByShift[j].Add(combined[j]);
                    }
                    else
                    {
                        combined[j] += s + delimiter;
                    }
                }
            }
        }

        foreach (var grams in gramsByShift)
        {
            LoadGrams(grams);
        }
    }

    public void LoadNGrams(string text, int order)
    {
        this.order = order;
        List<string> grams = new List<string>();

        int i = 0;
        while (i < text.Length)
        {
            string gram = text.UnicodeSafeSubstring(i, Math.Min(order, text.Length - i));
            grams.Add(gram);
            i += gram.Length;
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

            curr.prev.Add(prev.gram);

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
    public string GetNextGram(string prevGram, System.Random random, Func<MarkovState, string> getNext, bool tryToComplete = false, bool goForward = true, int tryTimes = 4)
    {
        MarkovState prev;
        if (!states.TryGetValue(prevGram, out prev))
            return "";

        string nextGram = "";

        int completeTries = 0;
        bool satisfied = false;
        while (!satisfied)
        {
            nextGram = getNext(prev);

            if (!tryToComplete)
            {
                satisfied = true;
            }
            else
            {
                completeTries++;
                if (goForward && (nextGram == END_GRAM.gram || states[nextGram].next.ContainsKey(END_GRAM.gram)))
                    satisfied = true;
                else if (!goForward && (nextGram == START_GRAM.gram || states[nextGram].prev.Contains(START_GRAM.gram)))
                    satisfied = true;
                else
                    satisfied = completeTries >= tryTimes;
            }
        }

        return nextGram;
    }

    private string GetNextRandomGram(MarkovState curr, Random random, bool weightedRandom = true)
    {
        if (weightedRandom)
        {
            int randomChoice = random.Next(0, curr.massCount);
            int countIncrement = 0;
            foreach (KeyValuePair<string, int> kvp in curr.next)
            {
                if (randomChoice >= countIncrement && randomChoice <= countIncrement + kvp.Value)
                {
                    return kvp.Key;
                }

                countIncrement += kvp.Value;
            }
        }
        else
        {
            int randomIndex = random.Next(0, curr.next.Count);
            foreach (string s in curr.next.Keys)
            {
                if (randomIndex == 0)
                {
                    return s;
                }
                randomIndex--;
            }
        }

        return null;
    }

    private string GetPrevRandomGram(MarkovState curr, Random random)
    {
        return curr.prev.ElementAt(random.Next(curr.prev.Count));
    }

    public string GenerateSequence(System.Random random, int preferredLength, bool insertSpace = false, int maxLength = 1000, bool weightedRandom = true)
    {
        string generatedText = "";

        string gram = START_GRAM.gram;

        bool satisfied = false;
        while (!satisfied)
        {
            gram = GetNextGram(gram, random, (x) => GetNextRandomGram(x, random, weightedRandom), generatedText.Length + (order == -1 ? 0 : order * 3) >= preferredLength);

            if (gram != END_GRAM.gram)
                generatedText += gram;

            if (insertSpace)
                generatedText += " ";

            if (gram == "" || gram == END_GRAM.gram || generatedText.Length > maxLength)
                satisfied = true;
        }

        return generatedText;
    }

    public string GenerateSequenceMiddleOut(string startGram, System.Random random, int preferredLength, bool insertSpace = false, int maxLength = 1000, bool weightedRandom = true, bool searchWithinGram = true)
    {
        if (!states.ContainsKey(startGram))
        {
            if (states.ContainsKey(startGram.ToLowerInvariant()))
            {
                startGram = startGram.ToLowerInvariant();
            }
            else if (searchWithinGram)
            {
                List<string> possibleGrams = new List<string>();
                foreach (var kvp in states)
                {
                    if (kvp.Key.ToLowerInvariant().Contains(startGram.ToLowerInvariant()))
                    {
                        possibleGrams.Add(kvp.Key);
                    }
                }

                if (possibleGrams.Count > 0)
                    startGram = possibleGrams[random.Next(possibleGrams.Count)];
            }
        }

        string generatedText = startGram;

        string backwardGram = startGram;
        string forwardGram = startGram;

        bool satisfied = false;
        while (!satisfied)
        {
            if (!string.IsNullOrEmpty(backwardGram) && backwardGram != START_GRAM.gram)
                backwardGram = GetNextGram(backwardGram, random, (x) => GetPrevRandomGram(x, random), generatedText.Length + (order == -1 ? 0 : order * 3) >= preferredLength, false);
            if (!string.IsNullOrEmpty(forwardGram) && forwardGram != END_GRAM.gram)
                forwardGram = GetNextGram(forwardGram, random, (x) => GetNextRandomGram(x, random, weightedRandom), generatedText.Length + (order == -1 ? 0 : order * 3) >= preferredLength);

            bool backwardDone = string.IsNullOrEmpty(backwardGram) || backwardGram == START_GRAM.gram;
            bool forwardDone = string.IsNullOrEmpty(forwardGram) || forwardGram == END_GRAM.gram;

            if (!forwardDone)
            {
                if (insertSpace)
                    generatedText += " ";
                generatedText += forwardGram;
            }

            if (!backwardDone)
            {
                if (insertSpace)
                    generatedText = " " + generatedText;
                generatedText = backwardGram + generatedText;
            }

            if ((backwardDone && forwardDone) || generatedText.Length > maxLength)
                satisfied = true;
        }

        return generatedText == startGram ? null : generatedText;
    }
}
