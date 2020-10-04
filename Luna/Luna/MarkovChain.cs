using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Luna.System;

public class MarkovChain
{
    public enum ScopeType
    {
        NONE = -1,
        QUOTES,
        PARENTHESIS,
        CURLY_BRACES,
        SQUARE_BRACKETS,

        DISCORD_HIDDEN,

        MD_ITALICS_ASTERISK,
        MD_ITALICS_UNDERSCORE,
        MD_BOLD,
        MD_BOLD_ITALICS,
        MD_UNDERLINE,
        MD_STRIKE_THROUGH,
        MD_CODE_LINE,
        MD_CODE_BLOCK,
        MD_QUOTE,
        MD_BLOCK_QUOTE
    }

    public class MarkovEdge
    {
        public ScopeType startScope, endScope;
        public int count;
        public string transition, from, to;
    }

    public const string START_GRAM = "THIS_IS_THE_STARTING_GRAM_BOOP_BEEP_BOOP1349857";
    public const string END_GRAM = "THIS_IS_THE_ENDING_GRAM_BOOP_BEEP_BOOP2462256";
    public const string LINK_GRAM = "%%LINK%%";
    public const string NUMBER_GRAM = "%%NUM-X%%";
    public const string NUMBER_GRAM_PATTERN = "%%NUM-(?<count>\\d+)%%";
    public const string USER_GRAM = "%%USER%%";

    readonly Dictionary<string, ScopeType> beginScopeGram = new Dictionary<string, ScopeType>()
    {
        {" \""  , ScopeType.QUOTES                },
        {"("    , ScopeType.PARENTHESIS           },
        {"{"    , ScopeType.CURLY_BRACES          },
        {"["    , ScopeType.SQUARE_BRACKETS       },

        {"||"   , ScopeType.DISCORD_HIDDEN        },

        {"*"    , ScopeType.MD_ITALICS_ASTERISK   },
        {"_"    , ScopeType.MD_ITALICS_UNDERSCORE },
        {"**"   , ScopeType.MD_BOLD               },
        {"***"  , ScopeType.MD_BOLD_ITALICS       },
        {"__"   , ScopeType.MD_UNDERLINE          },
        {"~~"   , ScopeType.MD_STRIKE_THROUGH     },
        {"`"    , ScopeType.MD_CODE_LINE          },
        {"```"  , ScopeType.MD_CODE_BLOCK         },
        {">"    , ScopeType.MD_QUOTE              },
        {">>>"  , ScopeType.MD_BLOCK_QUOTE        },
    };

    readonly Dictionary<string, ScopeType> endScopeGram = new Dictionary<string, ScopeType>()
    {
        {"\" "  , ScopeType.QUOTES                },
        {")"    , ScopeType.PARENTHESIS           },
        {"}"    , ScopeType.CURLY_BRACES          },
        {"]"    , ScopeType.SQUARE_BRACKETS       },

        {"||"   , ScopeType.DISCORD_HIDDEN        },

        {"*"    , ScopeType.MD_ITALICS_ASTERISK   },
        {"_ "   , ScopeType.MD_ITALICS_UNDERSCORE },
        {"**"   , ScopeType.MD_BOLD               },
        {"***"  , ScopeType.MD_BOLD_ITALICS       },
        {"__"   , ScopeType.MD_UNDERLINE          },
        {"~~"   , ScopeType.MD_STRIKE_THROUGH     },
        {"`"    , ScopeType.MD_CODE_LINE          },
        {"```"  , ScopeType.MD_CODE_BLOCK         },
        {"\n"   , ScopeType.MD_QUOTE              },
        //{null   , ScopeType.MD_BLOCK_QUOTE       },
    };

    //private string stripSymbols = " `~!@#$%^&*()_+-={}[]\\|;:\",./<>?\n\r\t";
    private const string LINK_PATTERN = @"((?:http|ftp)s?:\/\/[^\s]*)";
    private const string SPLIT_PATTERN = @"(```)|(`)|(~~)|([!$%^&])|(\*\*\*)|(\*\*)|(\*)|(\()|(\))|(__)|(_ )|(_)|([+\-={}[\]\\"",./<\?;:\|])|(>>>)|(>)|(\n)|(\r)|(\t)|( +)";
    private const string NUMBER_PATTERN = @"(\d+)";
    private static readonly Regex linksRegex                = new Regex(LINK_PATTERN);
    private static readonly Regex transitionRegex           = new Regex($"^({SPLIT_PATTERN})$");
    private static readonly Regex numberRegex               = new Regex($"^{NUMBER_PATTERN}$");
    private static readonly Regex numberPlaceholderRegex    = new Regex(NUMBER_GRAM_PATTERN);
    private static readonly Regex splitAndLinkRegex         = new Regex($"(<:\\w+:\\d+>)|{LINK_PATTERN}|({USER_GRAM})|{NUMBER_PATTERN}|{SPLIT_PATTERN}");

    public List<MarkovEdge> allEdges = new List<MarkovEdge>();
    public Dictionary<string, Dictionary<string, Dictionary<string, MarkovEdge>>> edgeFromToTrans = new Dictionary<string, Dictionary<string, Dictionary<string, MarkovEdge>>>();
    public Dictionary<string, Dictionary<string, Dictionary<string, MarkovEdge>>> edgeToFromTrans = new Dictionary<string, Dictionary<string, Dictionary<string, MarkovEdge>>>();
    public Dictionary<string, int> fromMassCount = new Dictionary<string, int>();
    public Dictionary<string, int> toMassCount = new Dictionary<string, int>();
    public List<string> allLinks = new List<string>();

    int order = -1;
    public int Order => order;

    public MarkovChain() { }

    public void ClearData()
    {
        allEdges.Clear();
        edgeFromToTrans.Clear();
        edgeToFromTrans.Clear();
        fromMassCount.Clear();
        toMassCount.Clear();
        allLinks.Clear();
        order = -1;
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

    [Obsolete]
    public bool LoadFromJsonLegacy(string json)
    {
        JObject root = JObject.Parse(json);

        foreach (JProperty entry in root.Properties())
        {
            string fromGram = entry.Name;

            JObject rhs = entry.Value as JObject;

            fromMassCount[fromGram] = (int) rhs.Property("count").Value;
            int checkCount = 0;

            JObject jNext = rhs.Property("next").Value as JObject;
            foreach (JProperty next in jNext.Properties())
            {
                string toGram = next.Name;
                int count = (int) next.Value;

                MarkovEdge edge = new MarkovEdge()
                {
                    count = count,
                    from = fromGram,
                    to = toGram,
                    transition = " ",
                    startScope = ScopeType.NONE,
                    endScope= ScopeType.NONE,
                };

                {
                    if (!edgeFromToTrans.TryGetValue(fromGram, out var toTransEdgesDict))
                        edgeFromToTrans[fromGram] = toTransEdgesDict = new Dictionary<string, Dictionary<string, MarkovEdge>>();
                    if (!toTransEdgesDict.TryGetValue(toGram, out var transitionDict))
                        toTransEdgesDict[toGram] = transitionDict = new Dictionary<string, MarkovEdge>();
                    transitionDict[" "] = edge;
                }
                {
                    if (!edgeToFromTrans.TryGetValue(toGram, out var fromTransEdgesDict))
                        edgeToFromTrans[toGram] = fromTransEdgesDict = new Dictionary<string, Dictionary<string, MarkovEdge>>();
                    if (!fromTransEdgesDict.TryGetValue(fromGram, out var transitionDict))
                        fromTransEdgesDict[fromGram] = transitionDict = new Dictionary<string, MarkovEdge>();
                    transitionDict[" "] = edge;
                }
                allEdges.Add(edge);

                if (!toMassCount.ContainsKey(toGram))
                {
                    toMassCount[toGram] = 1;
                }
                else
                {
                    toMassCount[toGram]++;
                }

                checkCount += count;
            }

            if (checkCount != fromMassCount[fromGram])
            {
                Console.WriteLine($"Number Mismatch: {checkCount} != {fromMassCount[fromGram]}\nUpdating total to {checkCount}");
                fromMassCount[fromGram] = checkCount;
            }
        }

        return true;
    }

    public bool LoadFromJson(string json)
    {
        JObject root = JObject.Parse(json);

        JArray linksArray = root.Property("links").Value as JArray;
        foreach (JToken jlink in linksArray)
        {
            allLinks.Add(jlink.Value<string>());
        }

        JArray edgesArray = root.Property("graph").Value as JArray;
        foreach (JObject entry in edgesArray)
        {
            string toGram           = (string)      entry.Property("to").Value;
            string fromGram         = (string)      entry.Property("from").Value;
            string transition       = (string)      entry.Property("transition").Value;
            int count               = (int)         entry.Property("#").Value;
            ScopeType startScope    = Enum.Parse<ScopeType>((string)entry.Property("startScope").Value);
            ScopeType endScope      = Enum.Parse<ScopeType>((string)entry.Property("endScope").Value);
            
            MarkovEdge edge = new MarkovEdge()
            {
                count = count,
                from = fromGram,
                to = toGram,
                transition = transition,
                startScope = startScope,
                endScope = endScope,
            };

            {
                if (!edgeFromToTrans.TryGetValue(fromGram, out var toTransEdgesDict))
                    edgeFromToTrans[fromGram] = toTransEdgesDict = new Dictionary<string, Dictionary<string, MarkovEdge>>();
                if (!toTransEdgesDict.TryGetValue(toGram, out var transitionDict))
                    toTransEdgesDict[toGram] = transitionDict = new Dictionary<string, MarkovEdge>();
                transitionDict[transition] = edge;
            }
            {
                if (!edgeToFromTrans.TryGetValue(toGram, out var fromTransEdgesDict))
                    edgeToFromTrans[toGram] = fromTransEdgesDict = new Dictionary<string, Dictionary<string, MarkovEdge>>();
                if (!fromTransEdgesDict.TryGetValue(fromGram, out var transitionDict))
                    fromTransEdgesDict[fromGram] = transitionDict = new Dictionary<string, MarkovEdge>();
                transitionDict[transition] = edge;
            }
            allEdges.Add(edge);

            if (!fromMassCount.ContainsKey(fromGram))
            {
                fromMassCount[fromGram] = count;
            }
            else
            {
                fromMassCount[fromGram] += count;
            }
            if (!toMassCount.ContainsKey(toGram))
            {
                toMassCount[toGram] = count;
            }
            else
            {
                toMassCount[toGram] += count;
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
            {
                writer.WritePropertyName("links");
                writer.WriteStartArray();
                foreach (string link in allLinks)
                {
                    writer.WriteValue(link);
                }
                writer.WriteEndArray();

                writer.WritePropertyName("graph");
                writer.WriteStartArray();
                foreach (MarkovEdge edge in allEdges)
                {
                    writer.WriteStartObject();
                    {
                        writer.WritePropertyName("from");
                        writer.WriteValue(edge.@from);

                        writer.WritePropertyName("transition");
                        writer.WriteValue(edge.transition);

                        writer.WritePropertyName("to");
                        writer.WriteValue(edge.to);

                        writer.WritePropertyName("#");
                        writer.WriteValue(edge.count);

                        writer.WritePropertyName("startScope");
                        writer.WriteValue(Enum.GetName(typeof(ScopeType), edge.startScope));

                        writer.WritePropertyName("endScope");
                        writer.WriteValue(Enum.GetName(typeof(ScopeType), edge.endScope));
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
    }

    public void LoadWordGramsFancy(string text)
    {
        if (text == null)
            return;

        string[] tokens = splitAndLinkRegex.Split(text);

        for (int i = 0; i < tokens.Length; i++)
        {
            if (linksRegex.IsMatch(tokens[i]))
            {
                allLinks.Add(tokens[i]);
                tokens[i] = LINK_GRAM;
            }
            else if (numberRegex.IsMatch(tokens[i]))
            {
                tokens[i] = NUMBER_GRAM.Replace("X", tokens[i].Length.ToString());
            }
        }

        string prev = START_GRAM;
        string curr = "";

        string prevTransition = null;
        ScopeType ptBegin = ScopeType.NONE;
        ScopeType ptEnd = ScopeType.NONE;

        string transition = "";
        ScopeType tBegin = ScopeType.NONE;
        ScopeType tEnd = ScopeType.NONE;

        void FindScopeTypes(string transition, out ScopeType beginScope, out ScopeType endScope)
        {
            beginScope = beginScopeGram.GetValueOrDefault(transition, ScopeType.NONE);
            endScope = endScopeGram.GetValueOrDefault(transition, ScopeType.NONE);
        }

        for (int i = 0; i < tokens.Length; i++)
        {
            string s = tokens[i];

            if (prev == "" && s == "")
            {
                continue;
            }

            if (!numberPlaceholderRegex.IsMatch(s) && (s == "" || transitionRegex.IsMatch(s))) // is a transition
            {
                if (curr != "" && prevTransition != null) // commit the last two words
                {
                    LoadGram(prev, curr, prevTransition, ptBegin, ptEnd);

                    prev = curr;
                    prevTransition = null;
                    ptBegin = ScopeType.NONE;
                    ptEnd = ScopeType.NONE;
                    curr = "";
                }

                {
                    FindScopeTypes(s, out ScopeType beginScope, out ScopeType endScope);
                    if (transition == null)
                    {
                        transition = s;
                        tBegin = beginScope;
                        tEnd = endScope;
                    }
                    else
                    {
                        transition += s;

                        if (tBegin == ScopeType.NONE)
                            tBegin = beginScope;
                        if (tEnd == ScopeType.NONE)
                            tEnd = endScope;

                        // find accumulative scope types
                        FindScopeTypes(transition, out beginScope, out endScope);
                        if (tBegin == ScopeType.NONE)
                            tBegin = beginScope;
                        if (tEnd == ScopeType.NONE)
                            tEnd = endScope;
                    }
                }
            }
            else if (prevTransition == null) // not a transition character, and a transition has been established
            {
                prevTransition = transition;
                ptBegin = tBegin;
                ptEnd = tEnd;
                transition = null;
                curr = s;
            }
        }

        if (curr == "" && transition != null) // word ended with transition characters like ?!)||
        {
            LoadGram(prev, END_GRAM, transition ?? "", tBegin, tEnd);
        }
        else // word ended with a word
        {
            LoadGram(prev, curr, prevTransition, ptBegin, ptEnd);
            LoadGram(curr, END_GRAM, "", ScopeType.NONE, ScopeType.NONE);
        }

        /*string prev = START_GRAM;
        string curr = "";
        string prevTransition = "";
        string transition = null;

        void FindScopeTypes(string transition, out ScopeType beginScope, out ScopeType endScope)
        {
            beginScope = beginScopeGram.GetValueOrDefault(transition, ScopeType.NONE);
            endScope = endScopeGram.GetValueOrDefault(transition, ScopeType.NONE);
        }

        for (int i = 0; i < text.Length; i++)
        {
            string s = text.UnicodeSafeSubstring(i, 1);

            if (s.Length == 1 && stripSymbols.IndexOf(s[0]) >= 0) // is a transition character
            {
                if (prevTransition != null) // commit the last two words
                {
                    FindScopeTypes(prevTransition, out ScopeType beginScope, out ScopeType endScope);
                    LoadGram(prev, curr, prevTransition, beginScope, endScope);

                    prev = curr;
                    prevTransition = null;
                    curr = "";
                }

                if (transition == null)
                {
                    transition = s;
                }
                else
                {
                    transition += s;
                }
            }
            else if (prevTransition == null) // not a transition character, and a transition has been established
            {
                prevTransition = transition;
                transition = null;
                curr = s;
            }
            else // no transition establish so keep appending to current
            {
                curr += s;
            }

            i += s.Length - 1; // account for any unicode offsets
        }

        if (curr == "" && transition != null) // word ended with transition characters like ?!)||
        {
            FindScopeTypes(transition ?? "", out ScopeType beginScope, out ScopeType endScope);
            LoadGram(prev, END_GRAM, transition ?? "", beginScope, endScope);
        }
        else // word ended with a word
        {
            FindScopeTypes(prevTransition, out ScopeType beginScope, out ScopeType endScope);
            LoadGram(prev, curr, prevTransition, beginScope, endScope);

            LoadGram(curr, END_GRAM, "", ScopeType.NONE, ScopeType.NONE);
        }*/
    }

    public void LoadGramsDelimiter(string text, string delimiter, int together = 1, int numShifts = 1)
    {
        if (numShifts <= 0)
        {
            Console.WriteLine("[MarkovChain] Num Shifts should be > 0");
            return;
        }

        if (string.IsNullOrEmpty(delimiter))
        {
            LoadGram(START_GRAM, text, "");
            LoadGram(text, END_GRAM, "");
        }
        else
        {
            string[] prev = new string[numShifts];
            for (int i = 0; i < numShifts; i++)
            {
                prev[i] = START_GRAM;
            }

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
                        LoadGram(prev[j], combined[j], i == 0 ? "" : delimiter);
                        prev[j] = combined[j];
                        combined[j] = "";
                    }
                    else if (i == grams.Length-1)
                    {
                        combined[j] += s;
                        prev[j] = combined[j];
                    }
                    else
                    {
                        combined[j] += s + delimiter;
                    }
                }
            }

            for (int i = 0; i < numShifts; i++)
            {
                LoadGram(prev[i], END_GRAM, "");
            }
        }
    }

    public void LoadNGrams(string text, int order)
    {
        this.order = order;
        string prev = START_GRAM;

        int i = 0;
        while (i < text.Length)
        {
            string gram = text.UnicodeSafeSubstring(i, Math.Min(order, text.Length - i));
            LoadGram(prev, gram, "");

            i += gram.Length;
            prev = gram;
        }
        LoadGram(prev, END_GRAM, "");
    }

    private void LoadGram(string from, string to, string transition, ScopeType startScope = ScopeType.NONE, ScopeType endScope = ScopeType.NONE)
    {
        bool dne = false;

        MarkovEdge edge;

        {
            if (!edgeFromToTrans.TryGetValue(from, out var toTransGramsDict))
            {
                edgeFromToTrans[from] = toTransGramsDict = new Dictionary<string, Dictionary<string, MarkovEdge>>();
            }

            if (!toTransGramsDict.TryGetValue(to, out var transitionDict))
            {
                toTransGramsDict[to] = transitionDict = new Dictionary<string, MarkovEdge>();
            }

            if (!transitionDict.TryGetValue(transition, out edge))
            {
                dne = true;
            }
        }

        {
            if (!edgeToFromTrans.TryGetValue(to, out var toTransGramsDict))
            {
                edgeToFromTrans[to] = toTransGramsDict = new Dictionary<string, Dictionary<string, MarkovEdge>>();
            }

            if (!toTransGramsDict.TryGetValue(from, out var transitionDict))
            {
                toTransGramsDict[from] = transitionDict = new Dictionary<string, MarkovEdge>();
            }
        }

        if (!fromMassCount.ContainsKey(from))
        {
            fromMassCount[from] = 1;
        }
        else
        {
            fromMassCount[from]++;
        }
        if (!toMassCount.ContainsKey(to))
        {
            toMassCount[to] = 1;
        }
        else
        {
            toMassCount[to]++;
        }

        if (dne)
        {
            edge = new MarkovEdge()
            {
                from = from,
                to = to,
                count = 1,
                transition = transition,
                startScope = startScope,
                endScope = endScope,
            };

            edgeFromToTrans[from][to][transition] = edge;
            edgeToFromTrans[to][from][transition] = edge;
            allEdges.Add(edge);
        }
        else
        {
            edge.count++;
        }
    }

    public void LoadGrams(List<string> grams)
    {
        string prev = START_GRAM;

        grams.Add(END_GRAM);
        foreach (string s in grams)
        {
           LoadGram(prev, s, " ");

            prev = s;
        }
        grams.RemoveAt(grams.Count-1);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="prevGram"></param>
    /// <param name="random">used for choosing the next gram</param>
    /// <param name="tryToFinish"></param>
    /// <returns></returns>
    public MarkovEdge GetNextEdge(string prevGram, System.Random random, Func<string, MarkovEdge> getNext, ScopeType precedingScope, ScopeType followingScope, bool tryToFinish = false, bool goForward = true, int tryTimes = 10)
    {
        if (tryTimes < 0)
        {
            return null;
        }

        MarkovEdge nextEdge = null;
        int satisfyTries = 0;
        bool satisfied = false;
        while (!satisfied)
        {
            satisfyTries++;
            nextEdge = getNext(prevGram);
            if (nextEdge == null)
            {
                if (satisfyTries > tryTimes)
                {
                    return null;
                }
                continue;
            }
            string nextGram = goForward ? nextEdge.to : nextEdge.@from;

            if (!tryToFinish)
            {
                if (goForward)
                {
                    if (nextEdge.endScope == precedingScope || precedingScope == ScopeType.NONE)
                    {
                        satisfied = true;
                    }
                    else
                    {
                        foreach (var kvp in edgeFromToTrans[nextGram])
                        {
                            foreach (var subKvp in kvp.Value)
                            {
                                if (subKvp.Value.endScope == precedingScope)
                                {
                                    satisfied = true;
                                    break;
                                }
                            }

                            if (satisfied) break;
                        }
                    }
                }
                else
                {
                    if (nextEdge.startScope == followingScope || followingScope == ScopeType.NONE)
                    {
                        satisfied = true;
                    }
                    else
                    {
                        foreach (var kvp in edgeFromToTrans[nextGram])
                        {
                            foreach (var subKvp in kvp.Value)
                            {
                                if (subKvp.Value.startScope == followingScope)
                                {
                                    satisfied = true;
                                    break;
                                }
                            }

                            if (satisfied) break;
                        }
                    }
                }
            }
            else
            {
                if (goForward && (nextGram == END_GRAM || edgeToFromTrans[nextGram].ContainsKey(END_GRAM)))
                {
                    satisfied = true;
                }
                else if (!goForward && (nextGram == START_GRAM || edgeFromToTrans[nextGram].ContainsKey(START_GRAM)))
                {
                    satisfied = true;
                }
            }

            if (!satisfied)
            {
                satisfied = satisfyTries >= tryTimes;
            }
        }

        return nextEdge;
    }

    public string GenerateRecursive(out bool success, string prevGram, System.Random random, Func<string, Random, HashSet<MarkovEdge>, MarkovEdge> getNext, ref Queue<ScopeType> precedingScope, ref Queue<ScopeType> followingScope, bool goForward = true, int numAttempts = 3, HashSet<MarkovEdge> cycleDetection = null)
    {
        if (cycleDetection == null)
        {
            cycleDetection = new HashSet<MarkovEdge>();
        }
        if (precedingScope == null)
        {
            precedingScope = new Queue<ScopeType>();
        }
        if (followingScope == null)
        {
            followingScope = new Queue<ScopeType>();
        }

        if (cycleDetection.Count > 300)
        {
            success = false;
            Console.WriteLine("too deep my dude");
            precedingScope = followingScope = null; // use as signal to abort
            return "";
        }

        if (prevGram == START_GRAM && !goForward)
        {
            success = followingScope.Count == 0;
            return "";
        }
        if (prevGram == END_GRAM && goForward)
        {
            success = precedingScope.Count == 0;
            return "";
        }

        string result = "";
        success = false;

        HashSet<MarkovEdge> ignoreEdges = new HashSet<MarkovEdge>();
        int attemptsCounter = numAttempts;
        while (attemptsCounter-- > 0)
        {
            MarkovEdge nextEdge = null;
            if (attemptsCounter > 0)
            {
                nextEdge = getNext(prevGram, random, ignoreEdges);
            }
            else
            {
                var nextEdgeDict = goForward ? edgeFromToTrans : edgeToFromTrans;
                foreach (var kvp in nextEdgeDict)
                {
                    foreach (var subKvp in kvp.Value)
                    {
                        foreach (var subSubKvp in subKvp.Value)
                        {
                            if (goForward && precedingScope.Count > 0 && precedingScope.Peek() == subSubKvp.Value.endScope)
                            {
                                nextEdge = subSubKvp.Value;
                                goto found;
                            }
                            if (!goForward && followingScope.Count > 0 && followingScope.Peek() == subSubKvp.Value.startScope)
                            {
                                nextEdge = subSubKvp.Value;
                                goto found;
                            }
                            if (goForward && precedingScope.Count == 0 && subSubKvp.Value.endScope == ScopeType.NONE && subSubKvp.Value.to == END_GRAM)
                            {
                                nextEdge = subSubKvp.Value;
                                goto found;
                            }
                            if (!goForward && followingScope.Count == 0 && subSubKvp.Value.startScope == ScopeType.NONE && subSubKvp.Value.to == END_GRAM)
                            {
                                nextEdge = subSubKvp.Value;
                                goto found;
                            }
                        }
                    }
                }
            }

            found:
            if (nextEdge == null || cycleDetection.Contains(nextEdge))
                break;

            Queue<ScopeType> tempPreceding = new Queue<ScopeType>(precedingScope);
            Queue<ScopeType> tempFollowing = new Queue<ScopeType>(followingScope);

            if (goForward)
            {
                if (nextEdge.endScope != ScopeType.NONE)
                {
                    if (tempPreceding.Count == 0)
                    {
                        tempFollowing.Enqueue(nextEdge.endScope);
                    }
                    else if (tempPreceding.Peek() == nextEdge.endScope)
                    {
                        tempPreceding.Dequeue();
                    }
                    else
                    {
                        continue;
                    }
                }
                else if (nextEdge.startScope != ScopeType.NONE)
                {
                    tempPreceding.Enqueue(nextEdge.startScope);
                }
            }
            else
            {
                if (nextEdge.startScope != ScopeType.NONE)
                {
                    if (tempFollowing.Count == 0)
                    {
                        tempPreceding.Enqueue(nextEdge.startScope);
                    }
                    else if (tempFollowing.Peek() == nextEdge.startScope)
                    {
                        tempFollowing.Dequeue();
                    }
                    else
                    {
                        continue;
                    }
                }
                else if (nextEdge.endScope != ScopeType.NONE)
                {
                    tempFollowing.Enqueue(nextEdge.endScope);
                }
            }

            cycleDetection.Add(nextEdge);
            string nextText = GenerateRecursive(out bool succeeded, goForward ? nextEdge.to : nextEdge.@from, random, getNext, ref tempPreceding, ref tempFollowing, goForward, numAttempts, cycleDetection);
            if (succeeded)
            {
                precedingScope = tempPreceding;
                followingScope = tempFollowing;
                if (goForward)
                {
                    result = nextEdge.transition + (nextEdge.to == END_GRAM ? "" : nextEdge.to + nextText);
                }
                else
                {
                    result = (nextEdge.@from == START_GRAM ? "" : nextText + nextEdge.@from) + nextEdge.transition;
                }
                success = true;
                break;
            }
            else
            {
                if (tempFollowing == null) // abort signal received
                {
                    followingScope = precedingScope = null;
                    success = false;
                    result = "";
                    Console.WriteLine("Aborting...");
                    break;
                }

                cycleDetection.Remove(nextEdge);
                ignoreEdges.Add(nextEdge);
            }
        }

        return result;
    }

    private MarkovEdge GetNextRandomEdge(string curr, Random random, HashSet<MarkovEdge> ignoreEdges = null)
    {
        int randomChoice = random.Next(0, fromMassCount[curr]);
        foreach (var kvp in edgeFromToTrans[curr])
        {
            foreach (var subKvp in kvp.Value)
            {
                if ((!ignoreEdges?.Contains(subKvp.Value) ?? true) && randomChoice <= subKvp.Value.count)
                {
                    return subKvp.Value;
                }

                randomChoice -= subKvp.Value.count;
            }
        }
        
        return null;
    }

    private MarkovEdge GetPrevRandomEdge(string curr, Random random, HashSet<MarkovEdge> ignoreEdges = null)
    {
        int randomChoice = random.Next(0, toMassCount[curr]);
        foreach (var kvp in edgeToFromTrans[curr])
        {
            foreach (var subKvp in kvp.Value)
            {
                if ((!ignoreEdges?.Contains(subKvp.Value) ?? true) && randomChoice <= subKvp.Value.count)
                {
                    return subKvp.Value;
                }

                randomChoice -= subKvp.Value.count;
            }
        }

        return null;
    }

    public string GenerateSequence(System.Random random, int preferredLength, int maxLength = 1000)
    {
        Queue<ScopeType> p = null, f = null;
        string result = GenerateRecursive(out bool success, START_GRAM, random, GetNextRandomEdge, ref p, ref f);
        if (success)
        {
            return result;
        }
        else
        {
            return "_ _";
        }

        /*
        string generatedText = "";

        string gram = START_GRAM;
        Queue<ScopeType> currentScope = new Queue<ScopeType>();
        currentScope.Enqueue(ScopeType.NONE);

        bool satisfied = false;
        while (!satisfied)
        {
            MarkovEdge edge = GetNextEdge(gram, random, (x) => GetNextRandomEdge(x, random), currentScope.Peek(), ScopeType.NONE, generatedText.Length + (order == -1 ? 0 : order * 3) >= preferredLength);
            if (edge == null) break;
            if (edge.to == END_GRAM && (string.IsNullOrEmpty(generatedText) || currentScope.Count > 1))
                continue;

            gram = edge.to;

            if (currentScope.Peek() != ScopeType.NONE && edge.endScope == currentScope.Peek())
            {
                currentScope.Dequeue();
            }
            else if (edge.startScope != ScopeType.NONE)
            {
                currentScope.Enqueue(edge.startScope);
            }

            if (gram != END_GRAM)
                generatedText += edge.transition + gram;

            if (gram == END_GRAM || generatedText.Length > maxLength)
                satisfied = true;
        }

        

        return generatedText;*/
    }

    public string GenerateSequenceMiddleOut(string startGram, System.Random random, int preferredLength, int maxLength = 1000, bool weightedRandom = true, bool searchWithinGram = true)
    {
        if (!edgeToFromTrans.ContainsKey(startGram))
        {
            if (edgeToFromTrans.ContainsKey(startGram.ToLowerInvariant()))
            {
                startGram = startGram.ToLowerInvariant();
            }
            else if (searchWithinGram)
            {
                List<string> possibleGrams = new List<string>();
                foreach (MarkovEdge edge in allEdges)
                {
                    if (edge.from.ToLowerInvariant().Contains(startGram.ToLowerInvariant()))
                    {
                        possibleGrams.Add(edge.from);
                    }
                }

                if (possibleGrams.Count > 0)
                {
                    startGram = possibleGrams[random.Next(possibleGrams.Count)];
                }
                else
                {
                    return null;
                }
            }
        }

        Queue<ScopeType> precedingScope = new Queue<ScopeType>();
        Queue<ScopeType> followingScope = new Queue<ScopeType>();
        string backResult = GenerateRecursive(out bool backSuccess, startGram, random, GetPrevRandomEdge, ref precedingScope, ref followingScope, false);
        string frontResult= GenerateRecursive(out bool frontSuccess, startGram, random, GetNextRandomEdge, ref precedingScope, ref followingScope);

        if (backSuccess && frontSuccess)
        {
            return backResult + startGram + frontResult;
        }
        else
        {
            return "_ _";
        }

        /*
                string generatedText = startGram;

                string backwardGram = startGram;
                string forwardGram = startGram;

                MarkovEdge backEdge = null;
                MarkovEdge forwardEdge = null;

                Queue<ScopeType> preceding = new Queue<ScopeType>();
                Queue<ScopeType> following = new Queue<ScopeType>();
                preceding.Enqueue(ScopeType.NONE);
                following.Enqueue(ScopeType.NONE);

                bool satisfied = false;
                while (!satisfied)
                {
                    bool skipBackward = false, skipForward = false;

                    if (!string.IsNullOrEmpty(backwardGram) && backwardGram != START_GRAM)
                    {
                        backEdge = GetNextEdge(backwardGram, random, (x) => GetPrevRandomEdge(x, random), preceding.Peek(), following.Peek(), generatedText.Length + (order == -1 ? 0 : order * 3) >= preferredLength, false);
                        if (backEdge != null)
                        {
                            if (backEdge.@from == START_GRAM && following.Count > 1)
                            {
                                skipBackward = true;
                            }
                            else
                            {
                                backwardGram = backEdge.@from;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(forwardGram) && forwardGram != END_GRAM)
                    {
                        forwardEdge = GetNextEdge(forwardGram, random, (x) => GetNextRandomEdge(x, random), preceding.Peek(), following.Peek(), generatedText.Length + (order == -1 ? 0 : order * 3) >= preferredLength);
                        if (forwardEdge != null)
                        {
                            if (forwardEdge.to == END_GRAM && preceding.Count > 1)
                            {
                                skipForward = true;
                            }
                            else
                            {
                                forwardGram = forwardEdge.to;
                            }
                        }
                    }

                    bool backwardDone = backEdge == null || backwardGram == null || backwardGram == START_GRAM;
                    bool forwardDone = forwardEdge == null || forwardGram == null || forwardGram == END_GRAM;

                    if (!forwardDone && !skipForward)
                    {
                        if (preceding.Peek() != ScopeType.NONE && forwardEdge.endScope == preceding.Peek())
                        {
                            preceding.Dequeue();
                        }
                        else if (forwardEdge.startScope != ScopeType.NONE)
                        {
                            preceding.Enqueue(forwardEdge.startScope);
                        }

                        generatedText += forwardEdge.transition + forwardGram;
                    }

                    if (!backwardDone && !skipBackward)
                    {
                        if (following.Peek() != ScopeType.NONE && backEdge.startScope == following.Peek())
                        {
                            following.Dequeue();
                        }
                        else if (backEdge.startScope != ScopeType.NONE)
                        {
                            following.Enqueue(backEdge.startScope);
                        }

                        generatedText = backwardGram + backEdge.transition + generatedText;
                    }

                    if ((backwardDone && forwardDone) || generatedText.Length > maxLength)
                        satisfied = true;
                }

                return generatedText == startGram ? null : generatedText;*/
    }

    public string ReplaceVariables(string text, Random r, List<string> usernames = null, List<string> overrideLinks = null, List<string> overrideNumbers = null)
    {
        if (text == null)
            return text;

        { // replace links
            List<string> linksList = overrideLinks == null ? allLinks : overrideLinks;
            int index;
            while ((index = text.IndexOf(LINK_GRAM)) >= 0)
            {
                string link = linksList.Count > 0 ? linksList[r.Next(linksList.Count)] : "http://www.schessgame.com/";
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

                    string number = overrideNumbers != null && overrideNumbers.Count > 0 ? overrideNumbers[r.Next(overrideNumbers.Count)] : r.Next(numDigits*10).ToString();
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
