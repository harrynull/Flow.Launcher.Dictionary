using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
//using System.Speech.Synthesis;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Flow.Launcher.Plugin;

namespace Dictionary
{
    public class Main : IAsyncPlugin, ISettingProvider, IResultUpdated
    {
        private ECDict ecdict;
        private WordCorrection wordCorrection;
        private Synonyms synonyms;
        private Iciba iciba;
        internal static PluginInitContext Context { get; private set; }
        private Settings settings;
        private DictDownloadManager dictDownloadManager;
        //private SpeechSynthesizer synth;

        private string ecdictLocation = Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Flow.Dictionary\ultimate.db");
        private string configLocation = Environment.ExpandEnvironmentVariables(@"%AppData%\FlowLauncher\Settings\Plugins\Flow.Dictionary\config.json");

        // These two are only for jumping in MakeResultItem
        private string ActionWord;
        private string QueryWord;

        public event ResultUpdatedEventHandler ResultsUpdated;

        public Control CreateSettingPanel()
        {
            return new DictionarySettings(settings);
        }

        public async Task InitAsync(PluginInitContext context)
        {
            string CurrentPath = context.CurrentPluginMetadata.PluginDirectory;

            Directory.CreateDirectory(Path.GetDirectoryName(configLocation));

            if (File.Exists(configLocation))
                settings = await JsonSerializer.DeserializeAsync<Settings>(File.OpenRead(configLocation)).ConfigureAwait(false);
            else
                settings = new Settings();
            settings.ConfigFile = configLocation;

            dictDownloadManager = new DictDownloadManager(ecdictLocation, context);
            wordCorrection = new WordCorrection(CurrentPath + "/dicts/frequency_dictionary_en_82_765.txt", settings.MaxEditDistance);
            synonyms = new Synonyms(settings.BighugelabsToken);
            iciba = new Iciba(settings.ICIBAToken);
            Context = context;
        }

        Result MakeResultItem(string title, string subtitle, string extraAction = null, string word = null)
        {
            string getWord() { return (word ?? QueryWord).Replace("!", ""); }
            // Return true if the user tries to copy (regradless of the result)
            bool CopyIfNeeded(ActionContext e)
            {
                if (!e.SpecialKeyState.AltPressed) return false;
                try
                {
                    Clipboard.SetDataObject(getWord());
                }
                catch (ExternalException ee)
                {
                    Context.API.ShowMsg("Copy failed, please try later", ee.Message);
                }
                return true;
            }
            /*
             Todo: System.Speech.Synthesis is not supported in .Net Core, need to find alternative. 
            https://github.com/dotnet/runtime/issues/30991
            bool ReadWordIfNeeded(ActionContext e)
            {
                if (!e.SpecialKeyState.CtrlPressed) return false;
                if (synth == null)
                {
                    synth = new SpeechSynthesizer();
                    synth.SetOutputToDefaultAudioDevice();
                }
                synth.SpeakAsync(getWord());
                return true;
            }
            */

            Func<ActionContext, bool> ActionFunc;
            if (extraAction != null)
            {
                ActionFunc = e =>
                {
                    if (CopyIfNeeded(e)) return true;
                    //if (ReadWordIfNeeded(e)) return false;
                    Context.API.ChangeQuery(ActionWord + " " + (word ?? QueryWord) + extraAction);
                    return false;
                };
            }
            else
            {
                ActionFunc = e =>
                {
                    if (CopyIfNeeded(e)) return true;
                    //if(ReadWordIfNeeded(e)) return false;
                    if (settings.WordWebsite != "") System.Diagnostics.Process.Start(string.Format(settings.WordWebsite, getWord()));
                    return true;
                };
            }
            return new Result()
            {
                Title = title,
                SubTitle = subtitle,
                IcoPath = "Images\\plugin.png",
                Action = ActionFunc
            };
        }

        private Result MakeWordResult(Word word) =>
            MakeResultItem(word.word, (word.phonetic != "" ? ("/" + word.phonetic + "/ ") : "") +
                (settings.ShowEnglishDefinition ? word.definition.Replace("\n", "; ") : word.translation.Replace("\n", "; ")),
                "!", word.word);

        private class WordEqualityComparer : IEqualityComparer<Result>
        {
            public static WordEqualityComparer instance = new WordEqualityComparer();

            public bool Equals([AllowNull] Result x, [AllowNull] Result y)
            {
                if (x.Equals(y))
                    return true;
                else
                    return x.Title == y.Title;

            }

            public int GetHashCode([DisallowNull] Result obj)
            {
                return obj.Title.GetHashCode();
            }
        }

        // First-level query.
        // English -> Chinese, supports fuzzy search.
        private async Task<List<Result>> FirstLevelQueryAsync(Query query, CancellationToken token)
        {
            string queryWord = query.Search;
            HashSet<Result> results = new HashSet<Result>(WordEqualityComparer.instance);

            // Pull fully match first.
            Word fullMatch = await ecdict.QueryAsync(query.Search, token).ConfigureAwait(false);
            if (fullMatch != null)
                results.Add(MakeWordResult(fullMatch));

            token.ThrowIfCancellationRequested();
            ResultsUpdated?.Invoke(this, new ResultUpdatedEventArgs { Results = results.ToList(), Query = query });

            // Then fuzzy search results. (since it's usually only a few)
            List<SymSpell.SuggestItem> suggestions = wordCorrection.Correct(queryWord);
            token.ThrowIfCancellationRequested();

            await foreach (var word in ecdict.QueryRange(suggestions.Select(x => x.term), token).Select(w => MakeWordResult(w)).ConfigureAwait(false))
            {
                results.Add(word);
            }

            token.ThrowIfCancellationRequested();
            ResultsUpdated?.Invoke(this, new ResultUpdatedEventArgs { Results = results.ToList(), Query = query });

            await foreach (var word in ecdict.QueryBeginningWith(queryWord, token).Select(w => MakeWordResult(w)).ConfigureAwait(false))
            {
                results.Add(word);
            }

            return results.ToList();
        }

        // Detailed information of a word.
        // English -> Phonetic, Translation, Definition, Exchanges, Synonym
        // Fuzzy search disabled.
        private async Task<List<Result>> DetailedQueryAsync(Query query, CancellationToken token)
        {
            string queryWord = query.Search[0..^1]; // Remove the !

            List<Result> results = new List<Result>();

            var word = await ecdict.QueryAsync(queryWord, token).ConfigureAwait(false);

            if (word.phonetic != "")
                results.Add(MakeResultItem(word.phonetic, "Phonetic"));
            if (word.translation != "")
                results.Add(MakeResultItem("Translation", word.translation.Replace("\n", "; "), "t"));
            if (word.definition != "")
                results.Add(MakeResultItem("Definition", word.definition.Replace("\n", "; "), "d"));
            if (word.exchange != "")
                results.Add(MakeResultItem("Exchanges", word.exchange, "e"));

            token.ThrowIfCancellationRequested();
            ResultsUpdated?.Invoke(this, new ResultUpdatedEventArgs
            {
                Query = query,
                Results = results
            });

            var synonymsResult = string.Join("; ", await synonyms.QueryAsync(word.word, token).ConfigureAwait(false));
            if (synonymsResult != "")
                results.Add(MakeResultItem("Synonym", synonymsResult, "s"));


            return results;
        }

        // Translations of a word.
        // English -> Translations
        // Fuzzy search disabled.
        private async Task<List<Result>> TranslationQueryAsync(Query query, CancellationToken token)
        {
            string queryWord = query.Search[0..^2]; // Get the word

            List<Result> results = new List<Result>();

            var word = await ecdict.QueryAsync(queryWord, token).ConfigureAwait(false);

            foreach (var translation in word.translation.Split('\n'))
            {
                results.Add(MakeResultItem(translation, "Translation"));
            }

            return results;
        }

        // Definitions of a word.
        // English -> Definitions
        // Fuzzy search disabled.
        private async Task<List<Result>> DefinitionQueryAsync(Query query, CancellationToken token)
        {
            string queryWord = query.Search[0..^2]; // Get the word

            List<Result> results = new List<Result>();

            var word = await ecdict.QueryAsync(queryWord, token).ConfigureAwait(false);

            foreach (var definition in word.definition.Split('\n'))
            {
                results.Add(MakeResultItem(definition, "Definitions"));
            }

            return results;
        }

        // Exchanges of a word.
        // English -> Exchanges
        // Fuzzy search disabled.
        private async Task<List<Result>> ExchangeQueryAsync(Query query, CancellationToken token)
        {
            string queryWord = query.Search[0..^2]; // Get the word

            List<Result> results = new List<Result>();

            var word = await ecdict.QueryAsync(queryWord, token);

            foreach (var exchange in word.exchange.Split('/'))
            {
                results.Add(MakeResultItem(exchange, "Exchanges"));
            }

            return results;
        }

        // Synonyms of a word.
        // English -> Synonyms
        // Fuzzy search disabled.
        // Internet access needed.
        private async Task<List<Result>> SynonymQueryAsync(Query query, CancellationToken token)
        {
            string queryWord = query.Search[0..^2]; // Get the word

            List<Result> results = new List<Result>();

            var syns = await synonyms.QueryAsync(queryWord, token).ConfigureAwait(false);

            return await ecdict.QueryRange(syns, token).Select(w => MakeWordResult(w)).ToListAsync(token);
        }

        // Chinese translation of a word.
        // English -> Synonyms
        // Fuzzy search disabled.
        // Internet access needed.
        private async Task<List<Result>> ChineseQueryAsync(Query query, CancellationToken token)
        {
            string queryWord = query.Search; // Get the word

            List<Result> results = new List<Result>();

            var translations = await iciba.QueryAsync(queryWord, token).ConfigureAwait(false);

            if (translations.Count == 0)
            {
                results.Add(MakeResultItem("No Results Found", queryWord));
            }
            else
            {
                foreach (var translation in translations)
                {
                    results.Add(MakeResultItem(translation, queryWord, "!", translation));
                }
            }

            return results;
        }

        private bool IsChinese(string cn)
        {
            foreach (char c in cn)
            {
                UnicodeCategory cat = char.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.OtherLetter)
                    return true;
            }
            return false;
        }

        public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            if (dictDownloadManager.NeedsDownload())
                return await dictDownloadManager.HandleQueryAsync(query).ConfigureAwait(false);

            ActionWord = query.ActionKeyword;
            string queryWord = query.Search;
            if (queryWord == "") return new List<Result>();
            QueryWord = queryWord;

            if (ecdict == null) ecdict = new ECDict(ecdictLocation);

            if (queryWord.Length < 2)
                return await FirstLevelQueryAsync(query, token).ConfigureAwait(false);

            return await (queryWord[^2..] switch
            {
                "!d" => DefinitionQueryAsync(query, token),
                "!t" => TranslationQueryAsync(query, token),
                "!e" => ExchangeQueryAsync(query, token),
                "!s" => SynonymQueryAsync(query, token),
                _ when queryWord[^1] == '!' => DetailedQueryAsync(query, token),
                _ when IsChinese(queryWord) => ChineseQueryAsync(query, token),
                _ => FirstLevelQueryAsync(query, token)
            }).ConfigureAwait(false);
        }
    }
}
