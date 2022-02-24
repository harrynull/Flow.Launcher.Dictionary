using Microsoft.Data.Sqlite;
using System;

namespace Dictionary
{
    class Word : IEquatable<Word>
    {
        public string word, translation, exchange, phonetic, definition;
        public Word(SqliteDataReader reader)
        {
            word = reader["word"].ToString();
            translation = reader["translation"].ToString();
            exchange = reader["exchange"].ToString();
            phonetic = reader["phonetic"].ToString();
            definition = reader["definition"].ToString();
        }

        public bool Equals(Word obj)
        {
            return obj != null && word == obj.word;
        }

        public override int GetHashCode()
        {
            return word.GetHashCode();
        }
    }
}
