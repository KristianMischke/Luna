using System;
using System.Collections.Generic;
using System.Text;

namespace Luna
{
    public class PlayerMarkovData
    {
        public readonly ulong pId;
        public MarkovChain wordChain;
        public MarkovChain nGramChain;

        public string MarkovWordPath => $"wordMarkov_{pId}.json";
        public string MarkovGramPath => $"gramMarkov_{pId}.json";

        public PlayerMarkovData(ulong pId)
        {
            this.pId = pId;

            wordChain = new MarkovChain();
            nGramChain = new MarkovChain();
        }
    }
}
