using System.IO;
using System.Text.Json;

namespace Dictionary
{
    public class Settings
    {
        public string ConfigFile = null!;
        public string ICIBAToken { get; set; } = "BEBC0A981CB63ED5198597D732BD8956";

        public string MerriamWebsterKey { get; set; } = "";

        public string BighugelabsToken { get; set; } = "";
        public int MaxEditDistance { get; set; } = 3;
        public bool ShowEnglishDefinition { get; set; }
        public string WordWebsite { get; set; } = "";

        public void Save()
        {
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(this));
        }
    }
}
