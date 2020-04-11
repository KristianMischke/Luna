using System;
using System.Collections.Generic;
using System.Text;

namespace Luna
{
    public class CustomUserData
    {
        public readonly ulong pId;

        public MarkovChain wordChain;
        public MarkovChain nGramChain;

        public const string wordMarkovPrefix = "wordMarkov_";
        public const string gramMarkovPrefix = "gramMarkov_";
        public string MarkovWordPath => $"{wordMarkovPrefix}{pId}.json";
        public string MarkovGramPath => $"{gramMarkovPrefix}{pId}.json";

        public bool TrackMe = false;

        public CustomUserData(ulong pId)
        {
            this.pId = pId;

            wordChain = new MarkovChain();
            nGramChain = new MarkovChain();
        }
    }
}
