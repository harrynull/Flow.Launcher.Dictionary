using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Dictionary
{
    public class Settings
    {
        public string ConfigFile;
        public string ICIBAToken { get; set; } = "BEBC0A981CB63ED5198597D732BD8956";
        public string BighugelabsToken { get; set; } = "";
        public int MaxEditDistance { get; set; } = 3;
        public bool ShowEnglishDefinition { get; set; } = false;
        public string WordWebsite { get; set; } = "";

        public void Save()
        {
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(this));
        }
    }
}
