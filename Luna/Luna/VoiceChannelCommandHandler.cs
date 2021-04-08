using System;
using System.Collections.Generic;
using System.Text;
// using System.Speech.Synthesis;
// using System.Speech.AudioFormat;
// using System.Speech.Recognition;
using System.Threading.Tasks;
using Discord;
using Discord.Audio;
using Discord.WebSocket;
using System.Globalization;
using System.Linq;
using Discord.Commands;
using System.Threading;

namespace Luna
{
    class VoiceChannelCommandHandler : ICustomCommandHandler
    {
        public static VoiceChannelCommandHandler _instance;

        private readonly DiscordSocketClient _client;

        private IAudioClient audioClient;
        private IAudioChannel audioChannel;
        private AudioOutStream audioOut;

        //SpeechSynthesizer synth;

        public VoiceChannelCommandHandler(DiscordSocketClient client)
        {
            _instance = this;
            _client = client;
        }


        public void OnJoinChannel(IAudioClient audioClient, IAudioChannel audioChannel)
        {
            this.audioClient = audioClient;
            this.audioChannel = audioChannel;

            audioClient.StreamCreated += AudioClient_StreamCreated;

            // if (synth == null)
            // {
            //     audioOut = audioClient.CreateDirectPCMStream(AudioApplication.Mixed);
            //
            //     synth = new SpeechSynthesizer();
            //     synth.Volume = 100;
            //     synth.Rate = 1;
            //
            //     foreach (var v in synth.GetInstalledVoices())
            //         Console.WriteLine(v.VoiceInfo.Name);
            //
            //     synth.SelectVoiceByHints(VoiceGender.Female, VoiceAge.Adult);
            //     synth.SetOutputToAudioStream(audioOut, new SpeechAudioFormatInfo(1920, AudioBitsPerSample.Sixteen, AudioChannel.Stereo));
            //     try
            //     {
            //         Task<string> mimicMsgTask = MimicCommandHandler._instance.GetMimicMessage(null, false, true);
            //         mimicMsgTask.Wait();
            //         synth.SpeakAsync(mimicMsgTask.Result);
            //     }
            //     finally
            //     {
            //         audioOut.FlushAsync();
            //     }
            // }


            Thread.Sleep(5000);
        }

        private async Task AudioClient_StreamCreated(ulong arg1, AudioInStream arg2)
        {
            Console.WriteLine(arg1);

            // using (SpeechRecognitionEngine recognizer = new SpeechRecognitionEngine(new CultureInfo("en-US")))
            // {
            //     // Create and load a dictation grammar.  
            //     recognizer.LoadGrammar(new DictationGrammar());
            //
            //     // Add a handler for the speech recognized event.  
            //     recognizer.SpeechRecognized += new EventHandler<SpeechRecognizedEventArgs>(recognizer_SpeechRecognized);
            //
            //     // Configure input to the speech recognizer.  
            //     recognizer.SetInputToAudioStream(arg2, new SpeechAudioFormatInfo(1920, AudioBitsPerSample.Sixteen, AudioChannel.Stereo));
            //
            //     // Start asynchronous, continuous speech recognition.  
            //     recognizer.RecognizeAsync(RecognizeMode.Multiple);
            //
            //     Thread.Sleep(5000);
            //     recognizer.RecognizeAsyncCancel();
            // }
        }

        // Handle the SpeechRecognized event.  
        // static void recognizer_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        // {
        //     Console.WriteLine("Recognized text: " + e.Result.Text);
        // }

        public void Cleanup()
        {
            
        }

        public async Task HandleMessageUpdated(Cacheable<IMessage, ulong> before, SocketMessage message, ISocketMessageChannel channel)
        {
            
        }

        public async Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> before, ISocketMessageChannel channel, SocketReaction reaction)
        {

        }

        public async Task HandleUserMessageAsync(SocketUserMessage message)
        {
            
        }

        public async Task SetupAsync()
        {
            
        }
    }
}
