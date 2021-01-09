using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dictionary
{
    class ECDict
    {
        readonly SQLiteConnection conn;
        public ECDict(string filename)
        {
            conn = new SQLiteConnection("Data Source=" + filename + ";Version=3;");
            conn.Open();
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
            string queryTerms = string.Join(',', words.Select(w => $"'{w.term}'"));
            if (queryTerms.Length == 0)
                yield break;

            string sql = $"select * from stardict where word in ({queryTerms})";


            using SQLiteCommand cmd = new SQLiteCommand(sql, conn);
            using SQLiteDataReader reader = cmd.ExecuteReader();

            while (reader.Read())
                yield return new Word(reader);

        }

        // This will include exact match and words beginning with it
        public IEnumerable<Word> QueryBeginningWith(string word, int limit = 20)
        {
            if (word.Length == 0) yield break;

            string sql = "select * from stardict where word like '" + word +
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
