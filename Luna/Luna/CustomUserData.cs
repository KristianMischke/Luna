using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LingK;
using Luna.Sentiment;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Luna
{
    public class CustomUserData
    {
        public const string HEADER_FILE = "header.json";
        public const string LINKS_FILE = "links.txt";
        public const string UNIGRAM_FILE = "unigram.tsv";
        public const string BIGRAM_FILE = "bigram.tsv";
        public const string TRIGRAM_FILE = "trigram.tsv";

        public readonly ulong pId;

        public List<string> linkList;
        public CoOccurrenceMatrix<string, string> unigramMatrix;
        public CoOccurrenceMatrix<(string, string), string> bigramMatrix;
        public CoOccurrenceMatrix<(string, string, string), string> trigramMatrix;
        public PMICalculator pmiCalc;

        public bool TrackMe { get { return (bool)(data["tracking"] ?? false); } set { data["tracking"] = value; } }

        public MoodProfile mood, lunasMoodWithUser;

        public JObject data;

        Regex bigramRegex = new Regex(@"^\((?<Item1>((?!, ).)*), (?<Item2>((?!, ).)*)\)$");
        private (string, string) ParseBigram(string value)
        {
            Match m = bigramRegex.Match(value);
            return (m.Groups["Item1"].Value, m.Groups["Item2"].Value);
        }
        Regex trigramRegex = new Regex(@"^\((?<Item1>((?!, ).)*), (?<Item2>((?!, ).)*), (?<Item3>((?!, ).)*)\)$");
        private (string, string, string) ParseTrigram(string value)
        {
            Match m = trigramRegex.Match(value);
            return (m.Groups["Item1"].Value, m.Groups["Item2"].Value, m.Groups["Item3"].Value);
        }

        public CustomUserData(ulong pId)
        {
            this.pId = pId;

            linkList = new List<string>();
            unigramMatrix = new CoOccurrenceMatrix<string, string>(row => row, row => row, col => col, col => col);
            bigramMatrix = new CoOccurrenceMatrix<(string, string), string>(row => row.ToString(), ParseBigram, col => col, col => col);
            trigramMatrix = new CoOccurrenceMatrix<(string, string, string), string>(row => row.ToString(), ParseTrigram, col => col, col => col);
            pmiCalc = new PMICalculator(unigramMatrix.GetColumn("all"), bigramMatrix.GetColumn("pmi_sentence"));

            mood = new MoodProfile();
            lunasMoodWithUser = new MoodProfile();

            data = new JObject();
        }

        public void ClearData()
        {
            unigramMatrix.Clear();
            bigramMatrix.Clear();
            trigramMatrix.Clear();
            linkList.Clear();
        }

        public async Task LoadDataAsync(string userDir)
        {
            if (!Directory.Exists(userDir))
            {
                return;
            }

            //---user 'header' data---
            try
            {
                using (StreamReader reader = new StreamReader(Path.Combine(userDir, HEADER_FILE)))
                {
                    data = await JObject.LoadAsync(new JsonTextReader(reader));
                }
                mood.LoadJson(data["mood"] as JObject);
                lunasMoodWithUser.LoadJson(data["lunasMoodWithUser"] as JObject);
            }
            catch (FileNotFoundException) { }
            //------

            //---links---
            try
            {
                linkList.Clear();
                using (StreamReader reader = new StreamReader(Path.Combine(userDir, LINKS_FILE)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        linkList.Add(line);
                    }
                }
            }
            catch (FileNotFoundException) { }
            //------

            try
            {
                unigramMatrix.Clear();
                unigramMatrix.Load(Path.Combine(userDir, UNIGRAM_FILE));
            }
            catch (FileNotFoundException) { }

            try
            {
                bigramMatrix.Clear();
                bigramMatrix.Load(Path.Combine(userDir, BIGRAM_FILE));
            }
            catch (FileNotFoundException) { }

            try
            {
                trigramMatrix.Clear();
                trigramMatrix.Load(Path.Combine(userDir, TRIGRAM_FILE));
            }
            catch (FileNotFoundException) { }
        }

        public void SaveData(string userDir)
        {
            Task t = SaveDataAsync(userDir);
            t.Wait();
        }

        public async Task SaveDataAsync(string userDir)
        {
            if (!Directory.Exists(userDir))
            {
                Directory.CreateDirectory(userDir);
            }

            //---user 'header' data---
            JsonWriter jwriter = data.CreateWriter();
            jwriter.WritePropertyName("mood");
            mood.WriteToJson(jwriter);
            jwriter.WritePropertyName("lunasMoodWithUser");
            lunasMoodWithUser.WriteToJson(jwriter);

            using (StreamWriter writer = new StreamWriter(Path.Combine(userDir, HEADER_FILE)))
            { 
                await data.WriteToAsync(new JsonTextWriter(writer));
            }
            //------

            //---save links---
            using (StreamWriter writer = new StreamWriter(Path.Combine(userDir, LINKS_FILE)))
            {
                foreach (string link in linkList)
                {
                    await writer.WriteLineAsync(link);
                }
            }
            //------

            //---save related models & data---
            unigramMatrix.Save(Path.Combine(userDir, UNIGRAM_FILE));
            bigramMatrix.Save(Path.Combine(userDir, BIGRAM_FILE));
            trigramMatrix.Save(Path.Combine(userDir, TRIGRAM_FILE));
            //------
        }
    }
}
