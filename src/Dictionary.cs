using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
//using System.Speech.Synthesis;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Flow.Launcher.Plugin;

namespace Dictionary
{
    public class Main : IPlugin, ISettingProvider, IResultUpdated
    {
        private ECDict ecdict;
        private WordCorrection wordCorrection;
        private Synonyms synonyms;
        private Iciba iciba;
        private PluginInitContext context;
        private Settings settings;
        //private SpeechSynthesizer synth;

        // These two are only for jumping in MakeResultItem
        private string ActionWord;
        private string QueryWord;

        public event ResultUpdatedEventHandler ResultsUpdated;

        public Control CreateSettingPanel()
        {
            return new DictionarySettings(settings);
        }

        public void Init(PluginInitContext context)
        {
            string CurrentPath = context.CurrentPluginMetadata.PluginDirectory;

            if (!Directory.Exists(Path.Combine(CurrentPath, "config")))
                Directory.CreateDirectory(Path.Combine(CurrentPath, "config"));

            string ConfigFile = CurrentPath + "/config/config.json";
            if (File.Exists(ConfigFile))
                settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(ConfigFile));
            else
                settings = new Settings();
            settings.ConfigFile = ConfigFile;

            ecdict = new ECDict(CurrentPath + "/dicts/ecdict.db");
            wordCorrection = new WordCorrection(CurrentPath + "/dicts/frequency_dictionary_en_82_765.txt", settings.MaxEditDistance);
            synonyms = new Synonyms(settings.BighugelabsToken);
            iciba = new Iciba(settings.ICIBAToken);
            this.context = context;
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
                    context.API.ShowMsg("Copy failed, please try later", ee.Message);
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
                    context.API.ChangeQuery(ActionWord + " " + (word ?? QueryWord) + extraAction);
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

        // First-level query.
        // English -> Chinese, supports fuzzy search.
        private List<Result> FirstLevelQuery(Query query)
        {
            string queryWord = query.Search;
            IEnumerable<Word> results = Enumerable.Empty<Word>();

            // Pull fully match first.
            Word fullMatch = ecdict.Query(query.Search);
            if (fullMatch != null)
                results = results.Append(fullMatch);

            // Then fuzzy search results. (since it's usually only a few)
            List<SymSpell.SuggestItem> suggestions = wordCorrection.Correct(queryWord);

            return results.Concat(ecdict.QueryRange(suggestions))
                          .Concat(ecdict.QueryBeginningWith(queryWord))
                          .Distinct()
                          .Select(w => MakeWordResult(w))
                          .ToList();
        }

        // Detailed information of a word.
        // English -> Phonetic, Translation, Definition, Exchanges, Synonym
        // Fuzzy search disabled.
        private List<Result> DetailedQuery(Query query)
        {
            string queryWord = query.Search[0..^1]; // Remove the !

            List<Result> results = new List<Result>();

            var word = ecdict.Query(queryWord);

            if (word.phonetic != "")
                results.Add(MakeResultItem(word.phonetic, "Phonetic"));
            if (word.translation != "")
                results.Add(MakeResultItem("Translation", word.translation.Replace("\n", "; "), "t"));
            if (word.definition != "")
                results.Add(MakeResultItem("Definition", word.definition.Replace("\n", "; "), "d"));
            if (word.exchange != "")
                results.Add(MakeResultItem("Exchanges", word.exchange, "e"));

            ResultsUpdated?.Invoke(this, new ResultUpdatedEventArgs
            {
                Query = query,
                Results = results
            });

            var synonymsResult = string.Join("; ", synonyms.Query(word.word));
            if (synonymsResult != "")
                results.Add(MakeResultItem("Synonym", synonymsResult, "s"));


            return results;
        }

        // Translations of a word.
        // English -> Translations
        // Fuzzy search disabled.
        private List<Result> TranslationQuery(Query query)
        {
            string queryWord = query.Search[0..^2]; // Get the word

            List<Result> results = new List<Result>();

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
        private List<Result> DefinitionQuery(Query query)
        {
            string queryWord = query.Search[0..^2]; // Get the word

            List<Result> results = new List<Result>();

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
        private List<Result> ExchangeQuery(Query query)
        {
            string queryWord = query.Search[0..^2]; // Get the word

            List<Result> results = new List<Result>();

            var word = ecdict.Query(queryWord);

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
        private List<Result> SynonymQuery(Query query)
        {
            string queryWord = query.Search[0..^2]; // Get the word

            List<Result> results = new List<Result>();

            var syns = synonyms.Query(queryWord);

            foreach (var syn in syns)
            {
                results.Add(MakeWordResult(ecdict.Query(syn)));
            }

            return results;
        }

        // Chinese translation of a word.
        // English -> Synonyms
        // Fuzzy search disabled.
        // Internet access needed.
        private List<Result> ChineseQuery(Query query)
        {
            string queryWord = query.Search; // Get the word

            List<Result> results = new List<Result>();

            var translations = iciba.Query(queryWord);

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

        public List<Result> Query(Query query)
        {
            ActionWord = query.ActionKeyword;
            string queryWord = query.Search;
            if (queryWord == "") return new List<Result>();
            QueryWord = queryWord;

            return queryWord.Substring(queryWord.Length - 2, 2) switch
            {
                "!d" => DefinitionQuery(query),
                "!t" => TranslationQuery(query),
                "!e" => ExchangeQuery(query),
                "!s" => SynonymQuery(query),
                _ when queryWord[^1] == '!' => DetailedQuery(query),
                _ when IsChinese(queryWord) => ChineseQuery(query),
                _ => FirstLevelQuery(query)
            };
        }
    }
}
