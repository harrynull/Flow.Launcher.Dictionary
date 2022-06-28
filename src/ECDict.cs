using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Dictionary
{
    class ECDict
    {
        readonly string connString;
        Regex stripWord = new Regex("[^a-zA-Z0-9]");

        public ECDict(string filename)
        {
            connString = new SqliteConnectionStringBuilder()
            {
                DataSource = filename,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = true
            }.ToString();
            
        }

        private string StripWord(string word)
        {
            return stripWord.Replace(word.Trim().ToLower(), "");
        }

        // This will only return exact match.
        // Return null if not found.
        public WordInformation? Query(string word)
        {
            if (word == "") return null;

            var sql = $"select * from stardict where word = \"{word.Replace("'", "''")}\"";
            
            WordInformation? ret = null;
            using var conn = new SqliteConnection(connString);
            conn.Open();
            using var cmd = new SqliteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            
            
            
            if (reader.Read())
                ret = new WordInformation(reader);

            return ret;
        }

        public async IAsyncEnumerable<WordInformation> QueryRange(IEnumerable<string> words, [EnumeratorCancellation] CancellationToken token)
        {
            var queryTerms = string.Join("','", words.Select(w => w.Replace("'", "''")));
            if (queryTerms.Length == 0)
                yield break;
            
            var sql = $"select * from stardict where word in ('{queryTerms}')";

            await using var conn = new SqliteConnection(connString);
            await conn.OpenAsync(token);
            await using var cmd = new SqliteCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false) as SqliteDataReader;

            while (await reader.ReadAsync(token).ConfigureAwait(false))
                yield return new WordInformation(reader);
        }

        // This will include exact match and words beginning with it
        public IEnumerable<WordInformation> QueryBeginningWith(string word, int limit = 20)
        {
            word = StripWord(word);
            if (word.Length == 0) yield break;

            var sql = $"select * from stardict where sw like '{word}%' order by frq > 0 desc, frq limit {limit}";

            using var conn = new SqliteConnection(connString); 
            conn.Open();
            using var cmd = new SqliteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return new WordInformation(reader);
            }
        }
    }
}
