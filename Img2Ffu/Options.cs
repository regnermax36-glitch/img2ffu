using CommandLine;

namespace Img2Ffu
{
    internal class Options
    {
        [Option("base-ffu", HelpText = "Path to the base FFU file (device-specific firmware). All sizes, platform IDs, OS version, device paths, etc. are auto-detected from this file.", Required = true)]
        public required string BaseFFU { get; set; }

        [Option("port-ffu", HelpText = "Path to the port FFU file (source of OS/partitions to port)", Required = true)]
        public required string PortFFU { get; set; }

        [Option('f', "ffu-file", HelpText = "Path to the output fully ported FFU file", Required = true)]
        public required string OutputFFUFile { get; set; }

        [Option("port-partitions", HelpText = "Optional comma-separated list of partitions to take from the port FFU (e.g. MainOS,Data). Default: all non-provisioning partitions.", Required = false)]
        public string? PortPartitions { get; set; }

        [Option("keep-provisioning", HelpText = "Always keep provisioning partitions from base (default true)", Default = true, Required = false)]
        public bool KeepProvisioning { get; set; } = true;

        [Option("temp-dir", HelpText = "Optional directory for temporary image files. Use a drive with enough free space (recommended for large FFUs). Defaults to system temp.", Required = false)]
        public string? TempDir { get; set; }

        [Option("inject", HelpText = "Optional path to a directory or file to inject into the MainOS partition of the output FFU (e.g. compiled shell folder).", Required = false)]
        public string? InjectPath { get; set; }

        [Option("inject-target", HelpText = "Target path inside MainOS for injection. Default: \\Windows\\SystemApps\\Windows12MobileShell", Default = "\\Windows\\SystemApps\\Windows12MobileShell", Required = false)]
        public string InjectTarget { get; set; } = "\\Windows\\SystemApps\\Windows12MobileShell";

        [Option('t', "secure-boot-signing-command", HelpText = "Optional command to sign the resulting FFU file", Default = "", Required = false)]
        public string SecureBootSigningCommand { get; set; } = "";
    }
}