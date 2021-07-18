using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Terrable
{
    public class Terrable
    {
        private readonly HttpClient _client;
        private readonly ILogger<Terrable> _logger;
        private static readonly SHA256 _sha = SHA256.Create();
        private static readonly string _homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        private readonly Dictionary<TerrableDirs, string> _dirs = new()
        {
            [TerrableDirs.Terrable] = Path.Join(_homeDir, "terrable"),
            [TerrableDirs.Versions] = Path.Join(_homeDir, "terrable", "versions"),
            [TerrableDirs.Temp] = Path.Join(_homeDir, "terrable", "temp")
        };

        public Terrable(HttpClient client, ILogger<Terrable> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task RunAsync(TerraformTarget target, bool force)
        {
            _logger.LogTrace("Creating directories");
            Directory.CreateDirectory(_dirs[TerrableDirs.Terrable]);
            Directory.CreateDirectory(_dirs[TerrableDirs.Versions]);
            Directory.CreateDirectory(_dirs[TerrableDirs.Temp]);

            if (File.Exists(Path.Join(_dirs[TerrableDirs.Versions], target.VersionedExcutableName)))
            {
                if (!force)
                {
                    _logger.LogInformation($"Version: {target.Version} already exists on disk. Swapping from cache");
                    OverwriteFromCached(target);
                    return;
                }

                _logger.LogInformation($"Version: {target.Version} was found in the cache but force was enabled. Overwriting");
            }

            _logger.LogInformation($"Attempting to download version {target.Version}");
            var archiveTempFile = Path.GetTempFileName();

            var archiveFile = await DownloadAsync(target.ArchiveUrl, archiveTempFile);

            if (archiveFile == null)
            {
                _logger.LogError($"Failed to download file - Is the version valid? {target.ArchiveUrl}");
            }

            _logger.LogInformation("Downloaded file successfully");
            var hashMatch = await CheckHashAsync(archiveFile.FullName, target);

            if (!hashMatch)
            {
                _logger.LogError("Cannot verify file hash, aborting ❌");
                return;
            }

            ZipFile.ExtractToDirectory(archiveTempFile, Path.Join(_dirs[TerrableDirs.Temp]), true);

            var extractedPath = Path.Join(_dirs[TerrableDirs.Temp], target.ExecutableName);
            var versionedTarget = Path.Join(_dirs[TerrableDirs.Versions], target.VersionedExcutableName);

            File.Move(extractedPath, versionedTarget, true);
            OverwriteFromCached(target);
            Directory.Delete(_dirs[TerrableDirs.Temp]);
        }

        //I47: Probably trace logging in this as well
        private void OverwriteFromCached(TerraformTarget target)
        {
            var versionedTarget = Path.Join(_dirs[TerrableDirs.Versions], target.VersionedExcutableName);
            var unversionedExe = Path.Join(_dirs[TerrableDirs.Terrable], target.ExecutableName);
            File.Copy(versionedTarget, unversionedExe, true);
        }

        //I47: Probably its own class and strategy here
        private async Task<bool> CheckHashAsync(string filePath, TerraformTarget target)
        {
            var hashList = await GetHashListAsync(target);
            var hash = GetHash(filePath);

            _logger.LogInformation($"File hash is: {hash}");
            _logger.LogTrace("Found Values: {@hashList}", hashList);

            if (!hashList.TryGetValue(hash, out var fileName))
            {
                _logger.LogError($"❌ - Cannot fetch hash with value '{hash}'");

                return false;
            }
            _logger.LogInformation($"Hash found that corresponds with archive file: {fileName}");

            if (!target.ArchiveFile.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("❌ - Hashes failed consistency check");
                return false;
            }

            _logger.LogInformation("✔ - Hashes pass consistency check");


            if (target.Hash != null && !hash.Equals(target.Hash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("❌ - Hash does not match the provided hash");
                return false;
            }

            _logger.LogInformation("✔ - Hash is good to go");
            return true;
        }

        private string GetHash(string path)
        {
            var fs = File.OpenRead(path);
            var hash = _sha.ComputeHash(fs);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        private async Task<FileInfo> DownloadAsync(Uri url, string targetPath)
        {
            _logger.LogInformation($"Downloading file {url} to {targetPath}");

            using var stream = await GetContentStream(url);

            if (stream == null)
                return null;

            using var fileStream = File.Open(targetPath, FileMode.Truncate);
            await stream.CopyToAsync(fileStream);

            return new FileInfo(targetPath);
        }

        private async Task<Dictionary<string, string>> GetHashListAsync(TerraformTarget target)
        {
            using var stream = await GetContentStream(target.ShaUrl);

            if (stream == null)
                return null;

            using var tr = new StreamReader(stream);
            var strings = await tr.ReadToEndAsync();

            var dict = new Dictionary<string, string>();
            foreach (var line in strings.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                dict[pair[0].Trim()] = pair[1].Trim();
            }

            return dict;
        }

        private async Task<Stream> GetContentStream(Uri url)
        {
            var res = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to access file at {url}. Response code: {res.StatusCode}");
                return null;
            }

            return await res.Content.ReadAsStreamAsync();
        }
    }
}
