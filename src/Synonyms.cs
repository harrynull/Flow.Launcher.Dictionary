using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dictionary
{
    class Synonyms
    {
        private readonly string ApiToken;
        public Synonyms(string apiToken)
        {
            ApiToken = apiToken;
        }
        public async Task<IEnumerable<string>> QueryAsync(string vocab, CancellationToken token)
        {
            try
            {
                var dataStream = await Main.Context.API.HttpGetStreamAsync($"http://words.bighugelabs.com/api/2/{ApiToken}/{vocab}/", token).ConfigureAwait(false);
                
                return ParseResult(new StreamReader(dataStream));
                
                static IEnumerable<string> ParseResult(StreamReader reader)
                {
                    using (reader)
                    {
                        while (!reader.EndOfStream)
                        {
                            var line = reader.ReadLine();
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            var parts = line.Split('|');
                            if (parts.Length <= 2) continue;
                            if (parts[1] == "syn") yield return parts[2];
                        }
                    }
                }
            }
            catch
            {
                return new List<string>();
            }
        }
    }
}
