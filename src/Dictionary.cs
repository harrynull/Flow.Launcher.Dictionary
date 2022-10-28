using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
//using System.Speech.Synthesis;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Flow.Launcher.Plugin;
using System.Threading.Tasks;

namespace Dictionary
{
    public class Main : IAsyncPlugin, ISettingProvider, IResultUpdated, ISavable
    {
        private ECDict ecdict = null!;
        private WordCorrection wordCorrection = null!;
        private Synonyms synonyms = null!;
        private Iciba iciba = null!;
        internal static PluginInitContext Context { get; private set; } = null!;
        private Settings settings = null!;
        private DictDownloadManager dictDownloadManager = null!;
        //private SpeechSynthesizer synth;

        private readonly string ecdictLocation = Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Flow.Dictionary\ultimate.db");
        private readonly string configLocation = Environment.ExpandEnvironmentVariables(@"%AppData%\FlowLauncher\Settings\Plugins\Flow.Dictionary\config.json");

        // These two are only for jumping in MakeResultItem
        private static string ActionWord => Context.CurrentPluginMetadata.ActionKeywords[0];
        private string QueryWord { get; set; } = "";

        public event ResultUpdatedEventHandler? ResultsUpdated;

        public Control CreateSettingPanel()
        {
            return new DictionarySettings(settings);
        }

        public async Task InitAsync(PluginInitContext context)
        {
            var currentPath = context.CurrentPluginMetadata.PluginDirectory ?? throw new ArgumentNullException("context.CurrentPluginMetadata.PluginDirectory");

            Directory.CreateDirectory(Path.GetDirectoryName(configLocation)!);
            

            if (File.Exists(configLocation))
            {
                await using var fileStream = File.OpenRead(configLocation);
                settings = (await JsonSerializer.DeserializeAsync<Settings>(fileStream).ConfigureAwait(false))!;
            }
            else
                settings = new Settings();
            settings.ConfigFile = configLocation;

            ecdict = new ECDict(ecdictLocation);
            dictDownloadManager = new DictDownloadManager(ecdictLocation);
            wordCorrection = new WordCorrection(currentPath + "/dicts/frequency_dictionary_en_82_765.txt", settings.MaxEditDistance);
            synonyms = new Synonyms(settings.BighugelabsToken);
            iciba = new Iciba(settings.ICIBAToken);
            Context = context;
            WebsterAudio.Api = context.API;
        }

        private Result MakeResultItem(string title, string subtitle, string? extraAction = null, string? word = null)
        {
            string GetWord() => (word ?? QueryWord).Replace("!", "");

            // Return true if the user tries to copy (regardless of the result)
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


            Func<ActionContext, bool> actionFunc;
            if (extraAction != null)
            {
                actionFunc = e =>
                {
                    if (CopyIfNeeded(e)) return true;
                    if (ReadWordIfNeeded(e)) return false;
                    Context.API.ChangeQuery(ActionWord + " " + (word ?? QueryWord) + extraAction);
                    return false;
                };
            }
            else
            {
                actionFunc = e =>
                {
                    if (CopyIfNeeded(e)) return true;
                    //if(ReadWordIfNeeded(e)) return false;
                    if (settings.WordWebsite != "") 
                    {
                      var ps = new System.Diagnostics.ProcessStartInfo(string.Format(settings.WordWebsite, GetWord())) 
                      {
                        UseShellExecute = true,
                        Verb = "open",
                      };
                      System.Diagnostics.Process.Start(ps);
                    }
                    return true;
                };
            }
            return new Result
            {
                Title = title,
                SubTitle = subtitle,
                IcoPath = "Images\\plugin.png",
                Action = actionFunc
            };
        }

        private Result MakeWordResult(WordInformation wordInformation) =>
            MakeResultItem(wordInformation.Word, (wordInformation.Phonetic != "" ? ("/" + wordInformation.Phonetic + "/ ") : "") +
                                      (settings.ShowEnglishDefinition ? wordInformation.Definition.Replace("\n", "; ") : wordInformation.Translation.Replace("\n", "; ")),
                "!", wordInformation.Word);

        private class WordEqualityComparer : IEqualityComparer<Result>
        {
            public static WordEqualityComparer Instance { get; }= new();

            public bool Equals(Result? x, Result? y)
            {
                if (x != null && x.Equals(y))
                    return true;

                return x?.Title ==
                       y?.Title;

            }

            public int GetHashCode(Result obj)
            {
                return obj.Title.GetHashCode();
            }
        }

        // First-level query.
        // English -> Chinese, supports fuzzy search.
        private async ValueTask<List<Result>> FirstLevelQueryAsync(Query query, CancellationToken token)
        {
            var queryWord = query.Search;
            var results = new HashSet<Result>(WordEqualityComparer.Instance);

            // Pull fully match first.
            var fullMatch = ecdict.Query(query.Search);
            if (fullMatch != null)
                results.Add(MakeWordResult(fullMatch));

            token.ThrowIfCancellationRequested();
            ResultsUpdated?.Invoke(this, new ResultUpdatedEventArgs
            {
                Results = results.ToList(), Query = query
            });

            // Then fuzzy search results. (since it's usually only a few)
            var suggestions = wordCorrection.Correct(queryWord);
            token.ThrowIfCancellationRequested();

            await foreach (var word in ecdict.QueryRange(suggestions.Select(x => x.term), token).Select(MakeWordResult).ConfigureAwait(false))
            {
                results.Add(word);
            }

            token.ThrowIfCancellationRequested();
            ResultsUpdated?.Invoke(this, new ResultUpdatedEventArgs
            {
                Results = results.ToList(), Query = query
            });

            foreach (var word in ecdict.QueryBeginningWith(queryWord).Select(MakeWordResult))
            {
                results.Add(word);
            }

            return results.ToList();
        }

        // Detailed information of a word.
        // English -> Phonetic, Translation, Definition, Exchanges, Synonym
        // Fuzzy search disabled.
        private async ValueTask<List<Result>> DetailedQueryAsync(Query query, CancellationToken token)
        {
            var queryWord = query.Search[0..^1]; // Remove the !

            var results = new List<Result>();

            var word = ecdict.Query(queryWord);

            if (word is null)
            {
                results.Add(MakeResultItem("No Results Found", queryWord));
                return results;
            }

            if (!string.IsNullOrWhiteSpace(word.Phonetic))
                results.Add(MakeResultItem(word.Phonetic, "Phonetic"));
            if (!string.IsNullOrWhiteSpace(word.Translation))
                results.Add(MakeResultItem("Translation", word.Translation.Replace("\n", "; "), "t"));
            if (!string.IsNullOrWhiteSpace(word.Definition))
                results.Add(MakeResultItem("Definition", word.Definition.Replace("\n", "; "), "d"));
            if (!string.IsNullOrWhiteSpace(word.Exchange))
                results.Add(MakeResultItem("Exchanges", word.Exchange, "e"));

            token.ThrowIfCancellationRequested();
            ResultsUpdated?.Invoke(this, new ResultUpdatedEventArgs
            {
                Query = query, Results = results
            });

            var synonymsResult = string.Join("; ", await synonyms.QueryAsync(word.Word, token).ConfigureAwait(false));
            if (synonymsResult != "")
                results.Add(MakeResultItem("Synonym", synonymsResult, "s"));


            return results;
        }
       

        // Translations of a word.
        // English -> Translations
        // Fuzzy search disabled.
        private ValueTask<List<Result>> TranslationQueryAsync(Query query)
        {
            var queryWord = query.Search[0..^2]; // Get the word

            var results = new List<Result>();

            var word = ecdict.Query(queryWord);

            if (word is null)
            {
                results.Add(MakeResultItem("No Results Found", queryWord));
                return ValueTask.FromResult(results);
            }
            
            foreach (var translation in word.Translation.Split('\n'))
            {
                results.Add(MakeResultItem(translation, "Translation"));
            }

            return ValueTask.FromResult(results);
        }

        // Definitions of a word.
        // English -> Definitions
        // Fuzzy search disabled.
        private ValueTask<List<Result>> DefinitionQueryAsync(Query query)
        {
            var queryWord = query.Search[0..^2]; // Get the word

            var results = new List<Result>();

            var word = ecdict.Query(queryWord);
            
            if (word is null)
            {
                results.Add(MakeResultItem("No Results Found", queryWord));
                return ValueTask.FromResult(results);
            }

            foreach (var definition in word.Definition.Split('\n'))
            {
                results.Add(MakeResultItem(definition, "Definitions"));
            }

            return ValueTask.FromResult(results);
        }

        // Exchanges of a word.
        // English -> Exchanges
        // Fuzzy search disabled.
        private ValueTask<List<Result>> ExchangeQueryAsync(Query query)
        {
            var queryWord = query.Search[0..^2]; // Get the word

            var word = ecdict.Query(queryWord);
            
            if (word is null)
            {
                return ValueTask.FromResult(new List<Result> { MakeResultItem("No Results Found", queryWord) });
            }

            return ValueTask.FromResult(word.Exchange.Split('/')
                .Select(exchange => MakeResultItem(exchange, "Exchanges"))
                .ToList());
        }

        // Synonyms of a word.
        // English -> Synonyms
        // Fuzzy search disabled.
        // Internet access needed.
        private async ValueTask<List<Result>> SynonymQueryAsync(Query query, CancellationToken token)
        {
            var queryWord = query.Search[..^2]; // Get the word

            var syns = await synonyms.QueryAsync(queryWord, token).ConfigureAwait(false);

            return await ecdict.QueryRange(syns, token).Select(MakeWordResult).ToListAsync(token);
        }

        // Chinese translation of a word.
        // English -> Synonyms
        // Fuzzy search disabled.
        // Internet access needed.
        private async ValueTask<List<Result>> ChineseQueryAsync(Query query, CancellationToken token)
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
                results.AddRange(translations.Select(translation => MakeResultItem(translation, queryWord, "!", translation)));
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

            var queryWord = query.Search;
            if (queryWord == "") return new List<Result>();
            QueryWord = queryWord;
            
            if (IsChinese(queryWord))
                return await ChineseQueryAsync(query, token);

            if (queryWord.Length < 2)
                return await FirstLevelQueryAsync(query, token).ConfigureAwait(false);

            return await (queryWord[^2..] switch
            {
                "!d" => DefinitionQueryAsync(query),
                "!t" => TranslationQueryAsync(query),
                "!e" => ExchangeQueryAsync(query),
                "!s" => SynonymQueryAsync(query, token),
                _ when queryWord[^1] == '!' => DetailedQueryAsync(query, token),
                _ => FirstLevelQueryAsync(query, token)
            });
        }
        public void Save()
        {
            settings.Save();
        }
    }
}