using Flow.Launcher.Plugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;

namespace Dictionary
{
    public class DictDownloadManager
    {
        private string dictPath;
        private bool downloading = false;
        private int downloadPercentage = 0;

        public DictDownloadManager(string dictPath)
        {
            this.dictPath = dictPath;
        }

        private async Task<bool> CheckForGoogleConnection()
        {
            try
            {
                var request = WebRequest.Create("https://google.com/generate_204");
                request.Timeout = 2000;
                await request.GetResponseAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> GetDownloadUrl()
        {
            bool shouldUseMirror = !await CheckForGoogleConnection();
            if (shouldUseMirror)
                return "https://download.fastgit.org/skywind3000/ECDICT-ultimate/releases/download/1.0.0/ecdict-ultimate-sqlite.zip";
            else
                return "https://github.com/skywind3000/ECDICT-ultimate/releases/download/1.0.0/ecdict-ultimate-sqlite.zip";
        }

        public async void PerformDownload()
        {
            downloading = true;

            var url = await GetDownloadUrl();
            var path = Path.GetDirectoryName(dictPath);
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            var client = new WebClient();

            client.DownloadProgressChanged += delegate (object sender, DownloadProgressChangedEventArgs e)
            {
                downloadPercentage = e.ProgressPercentage;
            };

            var tmpDictFileLoc = dictPath + ".download";
            await client.DownloadFileTaskAsync(new Uri(url), tmpDictFileLoc).ConfigureAwait(false);

            await Task.Run(() => ZipFile.ExtractToDirectory(tmpDictFileLoc, Path.GetDirectoryName(dictPath)));

            File.Delete(tmpDictFileLoc);

            downloading = false;
        }

        public async Task<List<Result>> HandleQueryAsync(Query query)
        {
            if (downloading)
            {
                var progress = "";
                if (downloadPercentage != 0) progress = $"{downloadPercentage} %";
                return new List<Result> { new Result() {
                    Title = $"Downloading dictionary database... {progress}",
                    SubTitle = "Press enter to refresh precentage.",
                    IcoPath = "Images\\plugin.png",
                    Action = (e) => {
                        Main.Context.API.ChangeQuery("d downloading" + new string('.',new Random().Next(0,10)));
                        return false;
                    }
                }};
            }
            else
            {
                return new List<Result> { new Result() {
                    Title = "Dictionary database not found (~1GB Decompressed)",
                    SubTitle = $"Press enter to download to {dictPath} (~230MB)",
                    IcoPath = "Images\\plugin.png",
                    Action = (e) =>
                    {
                        if(!downloading) PerformDownload();
                        Main.Context.API.ChangeQuery("d downloading");
                        return false;
                    }
                }};
            }
        }

        public bool NeedsDownload()
        {
            return !File.Exists(dictPath);
        }
    }
}
