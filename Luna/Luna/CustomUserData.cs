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
        public UnigramMatrix<int> unigramMatrix;
        public BigramMatrix<int> bigramMatrix;
        public TrigramMatrix<int> trigramMatrix;
        public PMICalculator pmiCalc;

        public bool TrackMe { get { return (bool)(data["tracking"] ?? false); } set { data["tracking"] = value; } }

        public string discordUsername { get { return (string)data["discordUsername"]; } set { data["discordUsername"] = value; } }

        public MoodProfile mood, lunasMoodWithUser;

        public JObject data;

        public CustomUserData(ulong pId)
        {
            this.pId = pId;

            linkList = new List<string>();
            unigramMatrix = new UnigramMatrix<int>(BasicTokenizer.Identity, BasicTokenizer.Identity, BasicTokenizer.Identity, BasicTokenizer.Identity);
            bigramMatrix = new BigramMatrix<int>(row => row.ToString(), BasicTokenizer.ParseBigram, BasicTokenizer.Identity, BasicTokenizer.Identity);
            trigramMatrix = new TrigramMatrix<int>(row => row.ToString(), BasicTokenizer.ParseTrigram, BasicTokenizer.Identity, BasicTokenizer.Identity);
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
