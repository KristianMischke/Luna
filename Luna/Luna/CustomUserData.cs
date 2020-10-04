using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Luna.Sentiment;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Luna
{
    public class CustomUserData
    {
        public readonly ulong pId;

        public MarkovChain doubleWordChain;
        public MarkovChain wordChain;
        public MarkovChain nGramChain;

        public const string wordMarkovPrefix = "wordMarkov_";
        public const string gramMarkovPrefix = "gramMarkov_";
        public string MarkovWordPath => $"{wordMarkovPrefix}{pId}.json";
        public string MarkovGramPath => $"{gramMarkovPrefix}{pId}.json";

        public bool TrackMe { get { return (bool)(data["tracking"] ?? false); } set { data["tracking"] = value; } }

        public MoodProfile mood, moodWithUser;

        public JObject data;

        public CustomUserData(ulong pId)
        {
            this.pId = pId;

            wordChain = new MarkovChain();
            nGramChain = new MarkovChain();
            doubleWordChain = new MarkovChain();

            mood = new MoodProfile();
            moodWithUser = new MoodProfile();

            data = new JObject();
        }

        public async Task LoadDataAsync(string path)
        {
            using (StreamReader reader = new StreamReader(path))
            {
                data = await JObject.LoadAsync(new JsonTextReader(reader));
            }
            mood.LoadJson(data["mood"] as JObject);
            moodWithUser.LoadJson(data["moodWithUser"] as JObject);
        }

        public void SaveData(string path)
        {
            Task t = SaveDataAsync(path);
            t.Wait();
        }

        public async Task SaveDataAsync(string path)
        {
            JsonWriter jwriter = data.CreateWriter();
            jwriter.WritePropertyName("mood");
            mood.WriteToJson(jwriter);
            jwriter.WritePropertyName("moodWithUser");
            moodWithUser.WriteToJson(jwriter);

            using (StreamWriter writer = new StreamWriter(path))
            { 
                await data.WriteToAsync(new JsonTextWriter(writer));
            }
        }
    }
}
