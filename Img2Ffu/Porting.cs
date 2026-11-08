using DiscUtils.Ntfs;
using Img2Ffu.Reader;
using Img2Ffu.Reader.Data;
using Img2Ffu.Writer;
using Img2Ffu.Writer.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Img2Ffu
{
    internal static class Porting
    {
        private static readonly HashSet<string> DefaultProvisioningPartitions = new(StringComparer.OrdinalIgnoreCase)
        {
            "DPP", "MODEM_FSG", "MODEM_FS1", "MODEM_FS2", "MODEM_FSC", "DDR", "SEC", "APDP", "MSADP", "DPO", "SSD", "DBI",
            "UEFI_BS_NV", "UEFI_NV", "UEFI_RT_NV", "UEFI_RT_NV_RPMB", "BOOTMODE", "LIMITS", "BACKUP_SBL1", "BACKUP_SBL2",
            "BACKUP_SBL3", "BACKUP_PMIC", "BACKUP_DBI", "BACKUP_UEFI", "BACKUP_RPM", "BACKUP_QSEE", "BACKUP_QHEE",
            "BACKUP_TZ", "BACKUP_HYP", "BACKUP_WINSECAPP", "BACKUP_TZAPPS", "BACKUP_PLAT", "BACKUP_ARM_EFIESP", "SVRawDump",
            "IS_UNLOCKED", "IS_UNLOCKED_SBL3", "BACKUP_BS_NV", "LogFS", "PMIC", "RESERVEDRO", "DSP", "BACKUP_TOOLSFV",
            "BACKUP_AOP", "BACKUP_DEVCFG", "BACKUP_RESERVEDRO", "BACKUP_DSP", "BACKUP_QUP", "BACKUP_DPO"
        };

        public static void CreatePortedFFU(Options options, ILogging logging)
        {
            if (!File.Exists(options.BaseFFU))
            {
                logging.Log($"Base FFU not found: {options.BaseFFU}", ILoggingLevel.Error);
                return;
            }
            if (!File.Exists(options.PortFFU))
            {
                logging.Log($"Port FFU not found: {options.PortFFU}", ILoggingLevel.Error);
                return;
            }

            logging.Log("Analyzing base FFU (full auto-detection of sizes, IDs, and metadata)...");
            using FileStream baseStream = File.OpenRead(options.BaseFFU);
            SignedImage baseSigned = new(baseStream);
            ImageFlash baseImage = baseSigned.Image;

            int storeCount = baseImage.Stores.Count;
            logging.Log($"Base FFU has {storeCount} store(s).");

            var metadata = ExtractMetadata(baseImage, baseSigned);
            logging.Log($"Detected Platform IDs: {string.Join(", ", metadata.PlatformIds)}");
            logging.Log($"Detected OS Version: {metadata.OsVersion}");
            logging.Log($"Detected Anti-Theft Version: {metadata.AntiTheftVersion}");
            logging.Log($"Detected Block Size: {metadata.BlockSize}");
            logging.Log($"Detected FFU Version: {metadata.FlashUpdateVersion}");

            HashSet<string> portPartitions = new(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(options.PortPartitions))
            {
                foreach (var p in options.PortPartitions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    portPartitions.Add(p);
            }

            string baseTemp = string.IsNullOrWhiteSpace(options.TempDir) ? Path.GetTempPath() : options.TempDir!;
            if (!Directory.Exists(baseTemp))
            {
                try { Directory.CreateDirectory(baseTemp); }
                catch (Exception ex)
                {
                    logging.Log($"Failed to create temp directory '{baseTemp}': {ex.Message}", ILoggingLevel.Error);
                    return;
                }
            }

            string tempDir = Path.Combine(baseTemp, "ffu_port_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            logging.Log($"Using temp directory: {tempDir}");
            logging.Log("Sparse low-space mode: temporary image is sparse (only real data blocks are allocated on disk).");

            try
            {
                List<InputForStore> inputs = new();

                for (int storeIndex = 0; storeIndex < storeCount; storeIndex++)
                {
                    logging.Log($"Processing store #{storeIndex + 1}...");

                    string baseImgPath = Path.Combine(tempDir, $"base_store{storeIndex}.img");

                    uint storeSectorSize;
                    string devicePath;
                    bool isFixed = true;

                    try
                    {
                        using (var reader = new FullFlashUpdateReaderStream(options.BaseFFU, (ulong)storeIndex))
                        {
                            storeSectorSize = reader.SectorSize > 0 ? (uint)reader.SectorSize : metadata.SectorSize;
                            devicePath = !string.IsNullOrEmpty(reader.DevicePath) ? reader.DevicePath : baseImage.Stores[storeIndex].DevicePath;
                            if (string.IsNullOrEmpty(devicePath))
                                devicePath = "VenHw(B615F1F5-5088-43CD-809C-A16E52487D00)";

                            logging.Log($"  Detected Sector Size: {storeSectorSize}");
                            logging.Log($"  Detected Device Path: {devicePath}");
                            logging.Log($"  Logical store size: {reader.Length / (1024.0 * 1024 * 1024):F1} GB (sparse file, only real data allocated)");

                            CreateSparseFile(baseImgPath, reader.Length);
                            using var fs = new FileStream(baseImgPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                            reader.CopyTo(fs, null);
                        }
                    }
                    catch (IOException ioEx) when (ioEx.Message.Contains("space") || ioEx.HResult == -2147024784)
                    {
                        logging.Log($"Not enough disk space while extracting base store to '{baseImgPath}'.", ILoggingLevel.Error);
                        logging.Log("Please free up space or specify a different drive with --temp-dir <path>", ILoggingLevel.Error);
                        throw;
                    }

                    try
                    {
                        using var portReader = new FullFlashUpdateReaderStream(options.PortFFU, (ulong)storeIndex);
                        using var baseFs = File.Open(baseImgPath, FileMode.Open, FileAccess.ReadWrite);

                        var baseParts = ParseGPT(baseFs, storeSectorSize);
                        var portParts = ParseGPT(portReader, storeSectorSize);

                        logging.Log($"  Base partitions: {string.Join(", ", baseParts.Select(p => p.Name))}");
                        logging.Log($"  Port partitions: {string.Join(", ", portParts.Select(p => p.Name))}");

                        foreach (var basePart in baseParts)
                        {
                            bool takeFromPort = portPartitions.Count > 0
                                ? portPartitions.Contains(basePart.Name)
                                : !DefaultProvisioningPartitions.Contains(basePart.Name);

                            if (options.KeepProvisioning && DefaultProvisioningPartitions.Contains(basePart.Name))
                                takeFromPort = false;

                            if (!takeFromPort)
                                continue;

                            var portPart = portParts.FirstOrDefault(p => string.Equals(p.Name, basePart.Name, StringComparison.OrdinalIgnoreCase));
                            if (portPart == null)
                            {
                                logging.Log($"  Partition {basePart.Name} not found in port, keeping from base.", ILoggingLevel.Warning);
                                continue;
                            }

                            long baseSize = (long)(basePart.LastSector - basePart.FirstSector + 1) * storeSectorSize;
                            long portSize = (long)(portPart.LastSector - portPart.FirstSector + 1) * storeSectorSize;

                            if (baseSize != portSize)
                            {
                                logging.Log($"  Partition {basePart.Name} size mismatch (base: {baseSize}, port: {portSize}), keeping from base.", ILoggingLevel.Warning);
                                continue;
                            }

                            logging.Log($"  Porting partition {basePart.Name} ({baseSize / (1024.0 * 1024):F0} MB)...");

                            long baseOffset = (long)basePart.FirstSector * storeSectorSize;
                            long portOffset = (long)portPart.FirstSector * storeSectorSize;

                            baseFs.Seek(baseOffset, SeekOrigin.Begin);
                            portReader.Seek(portOffset, SeekOrigin.Begin);

                            byte[] buffer = new byte[4 * 1024 * 1024];
                            long remaining = baseSize;
                            while (remaining > 0)
                            {
                                int toRead = (int)Math.Min(buffer.Length, remaining);
                                int read = portReader.Read(buffer, 0, toRead);
                                if (read == 0) break;
                                baseFs.Write(buffer, 0, read);
                                remaining -= read;
                            }
                        }

                        // Blank DPP so the writer sees only zeros and omits it (avoids Thor2 0x220002)
                        BlankPartition(baseFs, baseParts, "DPP", storeSectorSize, logging);

                        // Inject compiled shell into MainOS if requested
                        if (!string.IsNullOrWhiteSpace(options.InjectPath))
                        {
                            InjectIntoMainOS(baseFs, baseParts, storeSectorSize, options.InjectPath, options.InjectTarget, logging);
                        }
                    }
                    catch
                    {
                        logging.Log($"Port FFU does not have store #{storeIndex + 1} or failed to stream, keeping base as-is.", ILoggingLevel.Warning);
                    }

                    inputs.Add(new InputForStore(
                        baseImgPath,
                        devicePath,
                        isFixed,
                        0,
                        Array.Empty<string>()
                    ));
                }

                logging.Log("Generating ported FFU with fully auto-detected parameters (DPP blanked)...");
                List<Img2Ffu.Writer.Data.DeviceTargetInfo> deviceTargets = new();
                FFUFactory.GenerateFFU(
                    inputs,
                    options.OutputFFUFile,
                    metadata.PlatformIds,
                    metadata.SectorSize,
                    metadata.BlockSize,
                    metadata.AntiTheftVersion,
                    metadata.OsVersion,
                    metadata.FlashUpdateVersion,
                    deviceTargets,
                    options.SecureBootSigningCommand,
                    logging);

                logging.Log($"Successfully created ported FFU: {options.OutputFFUFile}");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static void InjectIntoMainOS(Stream imageStream, List<PartitionInfo> parts, uint sectorSize, string injectPath, string targetPath, ILogging logging)
        {
            var mainOs = parts.FirstOrDefault(p => string.Equals(p.Name, "MainOS", StringComparison.OrdinalIgnoreCase));
            if (mainOs == null)
            {
                logging.Log("MainOS partition not found, skipping injection.", ILoggingLevel.Warning);
                return;
            }

            long offset = (long)mainOs.FirstSector * sectorSize;
            long length = (long)(mainOs.LastSector - mainOs.FirstSector + 1) * sectorSize;

            logging.Log($"Injecting '{injectPath}' into MainOS at '{targetPath}'...");

            try
            {
                using var partStream = new SubStream(imageStream, offset, length);
                using var ntfs = new NtfsFileSystem(partStream);

                if (!Directory.Exists(injectPath) && !File.Exists(injectPath))
                {
                    logging.Log($"Inject path not found: {injectPath}", ILoggingLevel.Error);
                    return;
                }

                string dest = targetPath.Replace('/', '\\').TrimEnd('\\');
                if (!ntfs.DirectoryExists(dest))
                    ntfs.CreateDirectory(dest);

                if (File.Exists(injectPath))
                {
                    string fileName = Path.GetFileName(injectPath);
                    string destFile = dest + "\\" + fileName;
                    using var src = File.OpenRead(injectPath);
                    using var dst = ntfs.OpenFile(destFile, FileMode.Create, FileAccess.Write);
                    src.CopyTo(dst);
                    logging.Log($"  Injected file: {fileName}");
                }
                else
                {
                    foreach (var file in Directory.EnumerateFiles(injectPath, "*", SearchOption.AllDirectories))
                    {
                        string relative = Path.GetRelativePath(injectPath, file).Replace('/', '\\');
                        string destFile = dest + "\\" + relative;
                        string destDir = Path.GetDirectoryName(destFile)?.Replace('/', '\\') ?? dest;
                        if (!ntfs.DirectoryExists(destDir))
                            ntfs.CreateDirectory(destDir);

                        using var src = File.OpenRead(file);
                        using var dst = ntfs.OpenFile(destFile, FileMode.Create, FileAccess.Write);
                        src.CopyTo(dst);
                        logging.Log($"  Injected: {relative}");
                    }
                }

                logging.Log("Injection complete.");
            }
            catch (Exception ex)
            {
                logging.Log($"Injection failed: {ex.Message}", ILoggingLevel.Error);
            }
        }

        private class SubStream : Stream
        {
            private readonly Stream _base;
            private readonly long _offset;
            private readonly long _length;
            private long _position;

            public SubStream(Stream baseStream, long offset, long length)
            {
                _base = baseStream;
                _offset = offset;
                _length = length;
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => true;
            public override long Length => _length;
            public override long Position { get => _position; set => _position = value; }

            public override void Flush() => _base.Flush();
            public override int Read(byte[] buffer, int offset, int count)
            {
                _base.Seek(_offset + _position, SeekOrigin.Begin);
                int read = _base.Read(buffer, offset, (int)Math.Min(count, _length - _position));
                _position += read;
                return read;
            }
            public override long Seek(long offset, SeekOrigin origin)
            {
                _position = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => _position + offset,
                    SeekOrigin.End => _length + offset,
                    _ => throw new ArgumentException()
                };
                return _position;
            }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count)
            {
                _base.Seek(_offset + _position, SeekOrigin.Begin);
                _base.Write(buffer, offset, count);
                _position += count;
            }
        }

        private static void BlankPartition(Stream fs, List<PartitionInfo> parts, string name, uint sectorSize, ILogging logging)
        {
            var part = parts.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (part == null)
            {
                logging.Log($"  Partition {name} not found, nothing to blank.", ILoggingLevel.Warning);
                return;
            }

            long size = (long)(part.LastSector - part.FirstSector + 1) * sectorSize;
            long offset = (long)part.FirstSector * sectorSize;

            logging.Log($"  Blanking {name} partition ({size / (1024.0 * 1024):F0} MB) so Thor2 sees no data...");

            fs.Seek(offset, SeekOrigin.Begin);
            byte[] zero = new byte[4 * 1024 * 1024];
            long remaining = size;
            while (remaining > 0)
            {
                int toWrite = (int)Math.Min(zero.Length, remaining);
                fs.Write(zero, 0, toWrite);
                remaining -= toWrite;
            }
            fs.Flush();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        private const uint FSCTL_SET_SPARSE = 0x000900C4;

        private static void CreateSparseFile(string path, long length)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                uint bytesReturned;
                bool ok = DeviceIoControl(fs.SafeFileHandle.DangerousGetHandle(), FSCTL_SET_SPARSE,
                    IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);
                if (!ok)
                {
                    // Fall back to normal file if sparse fails
                }
            }
            fs.SetLength(length);
        }

        private class DetectedMetadata
        {
            public List<string> PlatformIds { get; set; } = new();
            public string OsVersion { get; set; } = "10.0.11111.0";
            public string AntiTheftVersion { get; set; } = "1.1";
            public uint BlockSize { get; set; } = 131072;
            public uint SectorSize { get; set; } = 512;
            public FlashUpdateVersion FlashUpdateVersion { get; set; } = FlashUpdateVersion.V1;
        }

        private static DetectedMetadata ExtractMetadata(ImageFlash image, SignedImage signed)
        {
            var meta = new DetectedMetadata();

            string manifest = image.ManifestData ?? "";
            foreach (string rawLine in manifest.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("DevicePlatformId0", StringComparison.OrdinalIgnoreCase))
                    meta.PlatformIds.Add(line.Split('=')[1].Trim());
                else if (line.StartsWith("DevicePlatformId1", StringComparison.OrdinalIgnoreCase))
                    meta.PlatformIds.Add(line.Split('=')[1].Trim());
                else if (line.StartsWith("DevicePlatformId2", StringComparison.OrdinalIgnoreCase))
                    meta.PlatformIds.Add(line.Split('=')[1].Trim());
                else if (line.StartsWith("DevicePlatformId3", StringComparison.OrdinalIgnoreCase))
                    meta.PlatformIds.Add(line.Split('=')[1].Trim());
                else if (line.StartsWith("OSVersion", StringComparison.OrdinalIgnoreCase))
                    meta.OsVersion = line.Split('=')[1].Trim();
                else if (line.StartsWith("AntiTheftVersion", StringComparison.OrdinalIgnoreCase))
                    meta.AntiTheftVersion = line.Split('=')[1].Trim();
                else if (line.StartsWith("SectorSize", StringComparison.OrdinalIgnoreCase))
                {
                    if (uint.TryParse(line.Split('=')[1].Trim(), out uint ss))
                        meta.SectorSize = ss;
                }
            }

            if (image.Stores.Count > 0)
            {
                var store = image.Stores[0];
                meta.BlockSize = store.StoreHeader.BlockSize > 0 ? store.StoreHeader.BlockSize : meta.BlockSize;

                if (store.FFUVersion == Reader.Enums.FFUVersion.V2)
                    meta.FlashUpdateVersion = FlashUpdateVersion.V2;
                else if (store.FFUVersion == Reader.Enums.FFUVersion.V1_COMPRESSED)
                    meta.FlashUpdateVersion = FlashUpdateVersion.V1;

                if (store.PlatformIDs.Any())
                    meta.PlatformIds = store.PlatformIDs.ToList();
            }

            if (meta.PlatformIds.Count == 0)
                meta.PlatformIds.Add("Microsoft.Generic.Platform");

            return meta;
        }

        private class PartitionInfo
        {
            public string Name { get; set; } = "";
            public ulong FirstSector { get; set; }
            public ulong LastSector { get; set; }
        }

        private static List<PartitionInfo> ParseGPT(Stream stream, uint sectorSize)
        {
            var parts = new List<PartitionInfo>();
            long originalPos = stream.Position;

            stream.Seek(0, SeekOrigin.Begin);
            byte[] headerBuf = new byte[Math.Max(512, (int)sectorSize * 2)];
            stream.Read(headerBuf, 0, headerBuf.Length);

            int efiPos = -1;
            for (int i = 0; i < headerBuf.Length - 8; i++)
            {
                if (Encoding.ASCII.GetString(headerBuf, i, 8) == "EFI PART")
                {
                    efiPos = i;
                    break;
                }
            }
            if (efiPos < 0)
            {
                stream.Seek(sectorSize, SeekOrigin.Begin);
                stream.Read(headerBuf, 0, headerBuf.Length);
                for (int i = 0; i < headerBuf.Length - 8; i++)
                {
                    if (Encoding.ASCII.GetString(headerBuf, i, 8) == "EFI PART")
                    {
                        efiPos = i;
                        break;
                    }
                }
            }
            if (efiPos < 0)
            {
                stream.Seek(originalPos, SeekOrigin.Begin);
                return parts;
            }

            uint maxPartitions = BitConverter.ToUInt32(headerBuf, efiPos + 0x50);
            uint entrySize = BitConverter.ToUInt32(headerBuf, efiPos + 0x54);
            ulong tableLba = BitConverter.ToUInt64(headerBuf, efiPos + 0x48);

            long tableOffset = (long)tableLba * sectorSize;
            stream.Seek(tableOffset, SeekOrigin.Begin);

            for (int i = 0; i < maxPartitions && i < 128; i++)
            {
                byte[] entry = new byte[entrySize];
                if (stream.Read(entry, 0, (int)entrySize) != entrySize)
                    break;

                bool empty = true;
                for (int j = 0; j < 16; j++) if (entry[j] != 0) empty = false;
                if (empty) break;

                string name = Encoding.Unicode.GetString(entry, 0x38, 72).TrimEnd('\0');
                if (string.IsNullOrEmpty(name)) break;

                ulong first = BitConverter.ToUInt64(entry, 0x20);
                ulong last = BitConverter.ToUInt64(entry, 0x28);

                parts.Add(new PartitionInfo { Name = name, FirstSector = first, LastSector = last });
            }

            stream.Seek(originalPos, SeekOrigin.Begin);
            return parts;
        }
    }
}