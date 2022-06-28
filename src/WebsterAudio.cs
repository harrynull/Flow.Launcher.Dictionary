using Flow.Launcher.Plugin;
using NAudio.Wave;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Dictionary
{
    public static class WebsterAudio
    {
        public static IPublicAPI? Api { get; set; }

        public static async Task Play(string word, string key)
        {
            if (Api == null)
            {
                throw new ArgumentNullException(nameof(Api));
            }
            
            if (string.IsNullOrEmpty(word))
            {
                return;
            }

            var url = $"https://dictionaryapi.com/api/v3/references/collegiate/json/{word}?key={key}";

            var responseStream = await Api.HttpGetStreamAsync(url);

            using var result = await JsonDocument.ParseAsync(responseStream);

            await EnumerateWebsterJson(word, result);
        }

        private static async Task RetrieveAudioAsync(JsonElement prs)
        {
            foreach (var pr in prs.EnumerateArray())
            {
                pr.GetProperty("mw").GetString();
                if (!pr.TryGetProperty("sound", out var sound))
                    continue;

                var audio = sound.GetProperty("audio").GetString();
                if (audio is null)
                {
                    continue;
                }
                var subDir = audio.StartsWith("bix") ? "bix" :
                    audio.StartsWith("gg") ? "gg" :
                    char.IsNumber(audio[0]) || audio[0] == '_' ? "number" :
                    audio[0].ToString();

                var url = $"https://media.merriam-webster.com/audio/prons/en/us/mp3/{subDir}/{audio}.mp3";
                await PlayMp3Url(url);
                return;
            }
        }

        private static async Task EnumerateWebsterJson(string search, JsonDocument document)
        {
            var root = document.RootElement;

            foreach (var entry in root.EnumerateArray())
            {
                var hwi = entry.GetProperty("hwi");
                var word = hwi.GetProperty("hw").GetString()!;
                if (search == word.Replace("*","") &&
                    hwi.TryGetProperty("prs", out var prs))
                {
                    await RetrieveAudioAsync(prs);
                    return;
                }

                var uros = entry.GetProperty("uros");
                foreach (var uro in uros.EnumerateArray())
                {
                    if (uro.GetProperty("ure").GetString()?.Replace("*", "") == search &&
                        uro.TryGetProperty("prs", out prs))
                    {
                        await RetrieveAudioAsync(prs);
                        return;
                    }
                }
            }
        }


        private static async Task PlayMp3Url(string url)
        {
            await using var mf = new MediaFoundationReader(url);
            using var player = new WaveOutEvent();
            player.Init(mf);
            player.Play();
            var tcs = new TaskCompletionSource();
            player.PlaybackStopped += (_, _) => tcs.SetResult();
            await tcs.Task;
        }
    }
}