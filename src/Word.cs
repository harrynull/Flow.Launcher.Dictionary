using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dictionary
{
    class Word : IEquatable<Word>
    {
        public string word, translation, exchange, phonetic, definition;
        public Word(SQLiteDataReader reader)
        {
            word = reader["word"].ToString();
            translation = reader["translation"].ToString();
            exchange = reader["exchange"].ToString();
            phonetic = reader["phonetic"].ToString();
            definition = reader["definition"].ToString();
        }

        public bool Equals(Word obj)
        {
            return word == obj.word;
        }

        public override int GetHashCode()
        {
            return word.GetHashCode();
        }
    }
}
