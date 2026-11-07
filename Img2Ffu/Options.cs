using CommandLine;
using Img2Ffu.Writer.Data;
using System.Collections.Generic;

namespace Img2Ffu
{
    internal class Options
    {
        [Option("base-ffu", HelpText = "Path to the base FFU file (device-specific firmware)", Required = true)]
        public required string BaseFFU { get; set; }

        [Option("port-ffu", HelpText = "Path to the port FFU file (source of OS/partitions to port)", Required = true)]
        public required string PortFFU { get; set; }

        [Option('f', "ffu-file", HelpText = "Path to the output fully ported FFU file", Required = true)]
        public required string OutputFFUFile { get; set; }

        [Option("port-partitions", HelpText = "Comma-separated list of partitions to take from the port FFU (default: all non-provisioning partitions)", Required = false)]
        public string? PortPartitions { get; set; }

        [Option('p', "plat-id", HelpText = "Platform ID to use (overrides base if specified)", Separator = ';', Required = false)]
        public IEnumerable<string>? PlatformIDs { get; set; }

        [Option('a', "anti-theft-version", HelpText = "Anti theft version (overrides base)", Required = false)]
        public string? AntiTheftVersion { get; set; }

        [Option('o', "os-version", HelpText = "Operating system version (overrides base)", Required = false)]
        public string? OperatingSystemVersion { get; set; }

        [Option('c', "block-size", HelpText = "Block size to use for the FFU file (overrides base)", Required = false)]
        public uint? BlockSize { get; set; }

        [Option('s', "sector-size", HelpText = "Sector size to use for the FFU file (overrides base)", Required = false)]
        public uint? SectorSize { get; set; }

        [Option('v', "full-flash-update-version", HelpText = "Version of the FFU file format to use (overrides base)", Required = false)]
        public FlashUpdateVersion? FlashUpdateVersion { get; set; }

        [Option('t', "secure-boot-signing-command", HelpText = "Optional command to sign the resulting FFU file", Default = "", Required = false)]
        public string SecureBootSigningCommand { get; set; } = "";

        [Option("keep-provisioning", HelpText = "Always keep provisioning partitions from base (default true)", Default = true, Required = false)]
        public bool KeepProvisioning { get; set; } = true;
    }
}