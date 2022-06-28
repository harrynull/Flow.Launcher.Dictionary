using Microsoft.Data.Sqlite;
using System;
using System.Data;

namespace Dictionary
{
    internal class WordInformation : IEquatable<WordInformation>
    {
        public readonly string Word, Translation, Exchange, Phonetic, Definition;
        public WordInformation(IDataRecord reader)
        {
            Word = reader["word"].ToString() ?? "";
            Translation = reader["translation"].ToString() ?? "";
            Exchange = reader["exchange"].ToString() ?? "";
            Phonetic = reader["phonetic"].ToString() ?? "";
            Definition = reader["definition"].ToString() ?? "";
        }

        public bool Equals(WordInformation? obj)
        {
            return obj != null && Word == obj.Word;
        }

        public override int GetHashCode()
        {
            return Word.GetHashCode();
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as WordInformation);
        }
    }
}