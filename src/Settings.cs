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
        public string ICIBAToken = "BEBC0A981CB63ED5198597D732BD8956";
        public string BighugelabsToken = "";
        public int MaxEditDistance = 3;
        public bool ShowEnglishDefinition = false;
        public string WordWebsite = "";

        public async void Save()
        {
            await JsonSerializer.SerializeAsync(File.OpenWrite(ConfigFile), this);
        }
    }
}
