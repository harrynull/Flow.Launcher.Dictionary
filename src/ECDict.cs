using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Dictionary
{
    class ECDict
    {
        readonly SQLiteConnection conn;
        Regex stripWord = new Regex("[^a-zA-Z0-9]");

        public ECDict(string filename)
        {
            conn = new SQLiteConnection("Data Source=" + filename + ";Version=3;Read Only=True");
            conn.Open();
        }

        public string StripWord(string word)
        {
            return stripWord.Replace(word.Trim().ToLower(), "");
        }

        // This will only return exact match.
        // Return null if not found.
        public async Task<Word> QueryAsync(string word, CancellationToken token)
        {
            if (word == "") return null;

            string sql = $"select * from stardict where word = \"{word.Replace("'", "''")}\"";

            Word ret = null;

            using SQLiteCommand cmd = new SQLiteCommand(sql, conn);
            using SQLiteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false) as SQLiteDataReader;

            if (reader.Read())
                ret = new Word(reader);

            return ret;
        }

        public async IAsyncEnumerable<Word> QueryRange(IEnumerable<string> words, [EnumeratorCancellation] CancellationToken token)
        {
            string queryTerms = string.Join("\",\"", words.Select(w => w.Replace("'", "''")));
            if (queryTerms.Length == 0)
                yield break;

            string sql = $"select * from stardict where word in (\"{queryTerms}\")";

            using SQLiteCommand cmd = new SQLiteCommand(sql, conn);
            using SQLiteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false) as SQLiteDataReader;

            while (await reader.ReadAsync(token).ConfigureAwait(false))
                yield return new Word(reader);
        }

        // This will include exact match and words beginning with it
        public async IAsyncEnumerable<Word> QueryBeginningWith(string word, [EnumeratorCancellation] CancellationToken token = default, int limit = 20)
        {
            word = StripWord(word);
            if (word.Length == 0) yield break;

            string sql = "select * from stardict where sw like \"" + word +
                "%\" order by frq > 0 desc, frq asc limit " + limit;

            using SQLiteCommand cmd = new SQLiteCommand(sql, conn);
            using SQLiteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false) as SQLiteDataReader;
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                yield return new Word(reader);
            }
        }
    }
}
