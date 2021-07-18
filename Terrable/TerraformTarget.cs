using System;
using System.IO;

namespace Terrable
{
    public class TerraformTarget
    {
        public string Version { get; set; }
        public string Platform { get; set; }
        public string Arch { get; set; }
        public string Hash { get; set; }

        public string ArchiveFile => $"terraform_{Version}_{Platform}_{Arch}.zip";
        public string ShaFile => $"terraform_{Version}_SHA256SUMS";
        public string FolderPath => $"{Version}";

        public string ExecutableName => Platform == "windows" ? "terraform.exe" : "terraform";
        public string VersionedExcutableName => $"terraform_{Version}";

        public Uri ShaUrl => new(Path.Join(BasePath.ToString(), FolderPath, ShaFile));
        public Uri ArchiveUrl => new(Path.Join(BasePath.ToString(), FolderPath, ArchiveFile));
        public static Uri BasePath => new($"https://releases.hashicorp.com/terraform/");
    }
}
