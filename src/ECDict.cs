using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dictionary
{
    class ECDict
    {
        readonly SQLiteConnection conn;
        Regex stripWord = new Regex("[^a-zA-Z0-9]");

        public ECDict(string filename)
        {
            conn = new SQLiteConnection("Data Source=" + filename + ";Version=3;");
            conn.Open();
        }

        public string StripWord(string word)
        {
            return stripWord.Replace(word.Trim().ToLower(), "");
        }

        // This will only return exact match.
        // Return null if not found.
        public Word Query(string word)
        {
            if (word == "") return null;

            string sql = $"select * from stardict where word = '{word}'";

            Word ret = null;

            using SQLiteCommand cmd = new SQLiteCommand(sql, conn);
            using SQLiteDataReader reader = cmd.ExecuteReader();

            if (reader.Read())
                ret = new Word(reader);

            return ret;
        }

        public IEnumerable<Word> QueryRange(IEnumerable<SymSpell.SuggestItem> words)
        {
            string queryTerms = string.Join(',', words.Select(w => $"'{StripWord(w.term)}'"));
            if (queryTerms.Length == 0)
                yield break;

            string sql = $"select * from stardict where sw in ({queryTerms})";


            using SQLiteCommand cmd = new SQLiteCommand(sql, conn);
            using SQLiteDataReader reader = cmd.ExecuteReader();

            while (reader.Read())
                yield return new Word(reader);

        }

        // This will include exact match and words beginning with it
        public IEnumerable<Word> QueryBeginningWith(string word, int limit = 20)
        {
            word = StripWord(word);
            if (word.Length == 0) yield break;

            string sql = "select * from stardict where sw like '" + word +
                "%' order by frq > 0 desc, frq asc limit " + limit;

            using SQLiteCommand cmd = new SQLiteCommand(sql, conn);
            using SQLiteDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return new Word(reader);
            }
        }
    }
}
