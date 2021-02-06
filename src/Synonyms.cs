using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
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
            List<string> ret = new List<string>();
            try
            {
                var dataStream = await Main.Context.API.HttpGetStreamAsync($"http://words.bighugelabs.com/api/2/{ApiToken}/{vocab}/", token).ConfigureAwait(false);

                using StreamReader reader = new StreamReader(dataStream);

                return ParseResult(reader);

                static IEnumerable<string> ParseResult(StreamReader reader)
                {
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (line == "") continue;
                        var parts = line.Split('|');
                        if (parts[1] == "syn") yield return parts[2];
                    }
                }
            }
            catch (Exception) { }
            return ret;
        }
    }
}
