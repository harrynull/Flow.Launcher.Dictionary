using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
//using System.Speech.Synthesis;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Flow.Launcher.Plugin;

namespace Dictionary
{
    public class Main : IAsyncPlugin, ISettingProvider, IResultUpdated, ISavable
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
            var CurrentPath = context.CurrentPluginMetadata.PluginDirectory;

            Directory.CreateDirectory(Path.GetDirectoryName(configLocation));

            if (File.Exists(configLocation))
            {
                await using var fileStream = File.OpenRead(configLocation);
                settings = await JsonSerializer.DeserializeAsync<Settings>(fileStream).ConfigureAwait(false);
            }
            else
                settings = new Settings();
            settings!.ConfigFile = configLocation;

            dictDownloadManager = new DictDownloadManager(ecdictLocation);
            wordCorrection = new WordCorrection(CurrentPath + "/dicts/frequency_dictionary_en_82_765.txt", settings.MaxEditDistance);
            synonyms = new Synonyms(settings.BighugelabsToken);
            iciba = new Iciba(settings.ICIBAToken);
            Context = context;
            WebsterAudio.api = context.API;
        }

        Result MakeResultItem(string title, string subtitle, string extraAction = null, string word = null)
        {
            string GetWord() { return (word ?? QueryWord).Replace("!", ""); }
            // Return true if the user tries to copy (regradless of the result)
            bool CopyIfNeeded(ActionContext e)
            {
                if (!e.SpecialKeyState.AltPressed) return false;
                try
                {
                    Clipboard.SetDataObject(GetWord());
                }
                catch (ExternalException ee)
                {
                    Context.API.ShowMsg("Copy failed, please try later", ee.Message);
                }
                return true;
            }

            bool ReadWordIfNeeded(ActionContext e)
            {
                if (!e.SpecialKeyState.CtrlPressed) return false;
                _ = WebsterAudio.Play(GetWord(), settings.MerriamWebsterKey);
                return true;
            }


            Func<ActionContext, bool> ActionFunc;
            if (extraAction != null)
            {
                ActionFunc = e =>
                {
                    if (CopyIfNeeded(e)) return true;
                    if (ReadWordIfNeeded(e)) return false;
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
                    if (settings.WordWebsite != "") System.Diagnostics.Process.Start(string.Format(settings.WordWebsite, GetWord()));
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
            var queryWord = query.Search;
            var results = new HashSet<Result>(WordEqualityComparer.instance);

            // Pull fully match first.
            var fullMatch = ecdict.Query(query.Search);
            if (fullMatch != null)
                results.Add(MakeWordResult(fullMatch));

            token.ThrowIfCancellationRequested();
            ResultsUpdated?.Invoke(this, new ResultUpdatedEventArgs { Results = results.ToList(), Query = query });

            // Then fuzzy search results. (since it's usually only a few)
            var suggestions = wordCorrection.Correct(queryWord);
            token.ThrowIfCancellationRequested();

            await foreach (var word in ecdict.QueryRange(suggestions.Select(x => x.term), token).Select(MakeWordResult).ConfigureAwait(false))
            {
                results.Add(word);
            }

            token.ThrowIfCancellationRequested();
            ResultsUpdated?.Invoke(this, new ResultUpdatedEventArgs { Results = results.ToList(), Query = query });

            foreach (var word in ecdict.QueryBeginningWith(queryWord).Select(MakeWordResult))
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
            var queryWord = query.Search[0..^1]; // Remove the !

            var results = new List<Result>();

            var word = ecdict.Query(queryWord);

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
            var queryWord = query.Search[0..^2]; // Get the word

            var results = new List<Result>();

            var word = ecdict.Query(queryWord);

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
            var queryWord = query.Search[0..^2]; // Get the word

            var results = new List<Result>();

            var word = ecdict.Query(queryWord);

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
            var queryWord = query.Search[0..^2]; // Get the word

            var word = ecdict.Query(queryWord);

            return word.exchange.Split('/')
                .Select(exchange => MakeResultItem(exchange, "Exchanges"))
                .ToList();
        }

        // Synonyms of a word.
        // English -> Synonyms
        // Fuzzy search disabled.
        // Internet access needed.
        private async Task<List<Result>> SynonymQueryAsync(Query query, CancellationToken token)
        {
            var queryWord = query.Search[0..^2]; // Get the word

            var results = new List<Result>();

            var syns = await synonyms.QueryAsync(queryWord, token).ConfigureAwait(false);

            return await ecdict.QueryRange(syns, token).Select(MakeWordResult).ToListAsync(token);
        }

        // Chinese translation of a word.
        // English -> Synonyms
        // Fuzzy search disabled.
        // Internet access needed.
        private async Task<List<Result>> ChineseQueryAsync(Query query, CancellationToken token)
        {
            var queryWord = query.Search; // Get the word

            var results = new List<Result>();

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
            foreach (var c in cn)
            {
                var cat = char.GetUnicodeCategory(c);
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
            var queryWord = query.Search;
            if (queryWord == "") return new List<Result>();
            QueryWord = queryWord;

            ecdict ??= new ECDict(ecdictLocation);

            if (IsChinese(queryWord))
                return await ChineseQueryAsync(query, token);
            
            if (queryWord.Length < 2)
                return await FirstLevelQueryAsync(query, token).ConfigureAwait(false);

            return await (queryWord[^2..] switch
            {
                "!d" => DefinitionQueryAsync(query, token),
                "!t" => TranslationQueryAsync(query, token),
                "!e" => ExchangeQueryAsync(query, token),
                "!s" => SynonymQueryAsync(query, token),
                _ when queryWord[^1] == '!' => DetailedQueryAsync(query, token),
                _ => FirstLevelQueryAsync(query, token)
            }).ConfigureAwait(false);
        }
        public void Save()
        {
            settings.Save();
        }
    }
}
