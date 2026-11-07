using Img2Ffu.Reader;
using Img2Ffu.Reader.Data;
using Img2Ffu.Writer;
using Img2Ffu.Writer.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            logging.Log("Analyzing base FFU...");
            using FileStream baseStream = File.OpenRead(options.BaseFFU);
            SignedImage baseSigned = new(baseStream);
            ImageFlash baseImage = baseSigned.Image;

            int storeCount = baseImage.Stores.Count;
            logging.Log($"Base FFU has {storeCount} store(s).");

            // Extract metadata from base
            var (platformIds, osVersion, antiTheft, blockSize, sectorSize, flashVersion) = ExtractMetadata(baseImage, baseSigned);

            // Overrides
            if (options.PlatformIDs != null && options.PlatformIDs.Any())
                platformIds = options.PlatformIDs.ToList();
            if (!string.IsNullOrEmpty(options.OperatingSystemVersion))
                osVersion = options.OperatingSystemVersion;
            if (!string.IsNullOrEmpty(options.AntiTheftVersion))
                antiTheft = options.AntiTheftVersion;
            if (options.BlockSize.HasValue)
                blockSize = options.BlockSize.Value;
            if (options.SectorSize.HasValue)
                sectorSize = options.SectorSize.Value;
            if (options.FlashUpdateVersion.HasValue)
                flashVersion = options.FlashUpdateVersion.Value;

            logging.Log($"Using Platform IDs: {string.Join(", ", platformIds)}");
            logging.Log($"OS Version: {osVersion}");
            logging.Log($"Block Size: {blockSize}, Sector Size: {sectorSize}, FFU Version: {flashVersion}");

            HashSet<string> portPartitions = new(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(options.PortPartitions))
            {
                foreach (var p in options.PortPartitions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    portPartitions.Add(p);
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "ffu_port_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            logging.Log($"Using temp directory: {tempDir}");

            try
            {
                List<InputForStore> inputs = new();

                for (int storeIndex = 0; storeIndex < storeCount; storeIndex++)
                {
                    logging.Log($"Processing store #{storeIndex + 1}...");

                    string baseImgPath = Path.Combine(tempDir, $"base_store{storeIndex}.img");
                    string portImgPath = Path.Combine(tempDir, $"port_store{storeIndex}.img");
                    string mergedImgPath = Path.Combine(tempDir, $"merged_store{storeIndex}.img");

                    // Extract base store
                    using (var reader = new FullFlashUpdateReaderStream(options.BaseFFU, (ulong)storeIndex))
                    {
                        using var fs = File.Create(baseImgPath);
                        reader.CopyTo(fs);
                    }

                    // Extract port store (assume same count)
                    try
                    {
                        using (var reader = new FullFlashUpdateReaderStream(options.PortFFU, (ulong)storeIndex))
                        {
                            using var fs = File.Create(portImgPath);
                            reader.CopyTo(fs);
                        }
                    }
                    catch
                    {
                        logging.Log($"Port FFU does not have store #{storeIndex + 1}, skipping port for this store.", ILoggingLevel.Warning);
                        File.Copy(baseImgPath, mergedImgPath, true);
                        // continue with base
                        goto AddInput;
                    }

                    // Merge
                    MergeImages(baseImgPath, portImgPath, mergedImgPath, portPartitions, options.KeepProvisioning, sectorSize, logging);

                AddInput:
                    var store = baseImage.Stores[storeIndex];
                    string devicePath = store.DevicePath;
                    if (string.IsNullOrEmpty(devicePath))
                    {
                        // Fallback for V1
                        devicePath = "VenHw(B615F1F5-5088-43CD-809C-A16E52487D00)";
                    }

                    inputs.Add(new InputForStore(
                        mergedImgPath,
                        devicePath,
                        true, // assume fixed
                        100, // default blank buffer
                        Array.Empty<string>() // no exclusions for the merged, as we already selected
                    ));
                }

                logging.Log("Generating ported FFU...");
                List<DeviceTargetInfo> deviceTargets = new();
                FFUFactory.GenerateFFU(
                    inputs,
                    options.OutputFFUFile,
                    platformIds,
                    sectorSize,
                    blockSize,
                    antiTheft,
                    osVersion,
                    flashVersion,
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

        private static (List<string> platformIds, string osVersion, string antiTheft, uint blockSize, uint sectorSize, FlashUpdateVersion flashVersion) ExtractMetadata(ImageFlash image, SignedImage signed)
        {
            var platformIds = new List<string>();
            string osVersion = "10.0.11111.0";
            string antiTheft = "1.1";
            uint blockSize = 131072;
            uint sectorSize = 512;
            FlashUpdateVersion flashVersion = FlashUpdateVersion.V1;

            // Parse manifest
            string manifest = image.ManifestData;
            foreach (string line in manifest.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("DevicePlatformId0", StringComparison.OrdinalIgnoreCase))
                    platformIds.Add(line.Split('=')[1].Trim());
                else if (line.StartsWith("DevicePlatformId1", StringComparison.OrdinalIgnoreCase))
                    platformIds.Add(line.Split('=')[1].Trim());
                else if (line.StartsWith("DevicePlatformId2", StringComparison.OrdinalIgnoreCase))
                    platformIds.Add(line.Split('=')[1].Trim());
                else if (line.StartsWith("DevicePlatformId3", StringComparison.OrdinalIgnoreCase))
                    platformIds.Add(line.Split('=')[1].Trim());
                else if (line.StartsWith("OSVersion", StringComparison.OrdinalIgnoreCase))
                    osVersion = line.Split('=')[1].Trim();
                else if (line.StartsWith("AntiTheftVersion", StringComparison.OrdinalIgnoreCase))
                    antiTheft = line.Split('=')[1].Trim();
                else if (line.StartsWith("SectorSize", StringComparison.OrdinalIgnoreCase))
                {
                    if (uint.TryParse(line.Split('=')[1].Trim(), out uint ss))
                        sectorSize = ss;
                }
            }

            if (image.Stores.Count > 0)
            {
                var store = image.Stores[0];
                blockSize = store.StoreHeader.BlockSize;
                if (store.FFUVersion == Reader.Enums.FFUVersion.V2)
                    flashVersion = FlashUpdateVersion.V2;
                if (store.PlatformIDs.Any())
                    platformIds = store.PlatformIDs;
            }

            if (platformIds.Count == 0)
                platformIds.Add("Microsoft.Generic.Platform");

            return (platformIds, osVersion, antiTheft, blockSize, sectorSize, flashVersion);
        }

        private static void MergeImages(string baseImg, string portImg, string mergedImg, HashSet<string> portPartitions, bool keepProvisioning, uint sectorSize, ILogging logging)
        {
            File.Copy(baseImg, mergedImg, true);

            using var baseFs = File.Open(mergedImg, FileMode.Open, FileAccess.ReadWrite);
            using var portFs = File.OpenRead(portImg);

            var baseParts = ParseGPT(baseFs, sectorSize);
            var portParts = ParseGPT(portFs, sectorSize);

            logging.Log($"Base partitions: {string.Join(", ", baseParts.Select(p => p.Name))}");
            logging.Log($"Port partitions: {string.Join(", ", portParts.Select(p => p.Name))}");

            foreach (var basePart in baseParts)
            {
                bool takeFromPort = false;
                if (portPartitions.Count > 0)
                {
                    takeFromPort = portPartitions.Contains(basePart.Name);
                }
                else
                {
                    // Default: take from port if not provisioning
                    takeFromPort = !DefaultProvisioningPartitions.Contains(basePart.Name);
                }

                if (keepProvisioning && DefaultProvisioningPartitions.Contains(basePart.Name))
                    takeFromPort = false;

                if (!takeFromPort)
                    continue;

                var portPart = portParts.FirstOrDefault(p => string.Equals(p.Name, basePart.Name, StringComparison.OrdinalIgnoreCase));
                if (portPart == null)
                {
                    logging.Log($"Partition {basePart.Name} not found in port, keeping from base.", ILoggingLevel.Warning);
                    continue;
                }

                long baseSize = (long)(basePart.LastSector - basePart.FirstSector + 1) * sectorSize;
                long portSize = (long)(portPart.LastSector - portPart.FirstSector + 1) * sectorSize;

                if (baseSize != portSize)
                {
                    logging.Log($"Partition {basePart.Name} size mismatch (base: {baseSize}, port: {portSize}), keeping from base.", ILoggingLevel.Warning);
                    continue;
                }

                logging.Log($"Porting partition {basePart.Name} ({baseSize} bytes)...");

                // Copy data
                long baseOffset = (long)basePart.FirstSector * sectorSize;
                long portOffset = (long)portPart.FirstSector * sectorSize;

                baseFs.Seek(baseOffset, SeekOrigin.Begin);
                portFs.Seek(portOffset, SeekOrigin.Begin);

                byte[] buffer = new byte[1024 * 1024];
                long remaining = baseSize;
                while (remaining > 0)
                {
                    int toRead = (int)Math.Min(buffer.Length, remaining);
                    int read = portFs.Read(buffer, 0, toRead);
                    if (read == 0) break;
                    baseFs.Write(buffer, 0, read);
                    remaining -= read;
                }
            }
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
            stream.Seek(0, SeekOrigin.Begin);
            byte[] headerBuf = new byte[Math.Max(512, (int)sectorSize * 2)];
            stream.Read(headerBuf, 0, headerBuf.Length);

            // Find EFI PART
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
                // Try at LBA 1
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
                return parts;

            // Simplified: assume standard layout, read entries starting after header
            uint maxPartitions = BitConverter.ToUInt32(headerBuf, efiPos + 0x50);
            uint entrySize = BitConverter.ToUInt32(headerBuf, efiPos + 0x54);
            ulong tableLba = BitConverter.ToUInt64(headerBuf, efiPos + 0x48); // Partition entry LBA

            long tableOffset = (long)tableLba * sectorSize;
            stream.Seek(tableOffset, SeekOrigin.Begin);

            for (int i = 0; i < maxPartitions && i < 128; i++) // safety
            {
                byte[] entry = new byte[entrySize];
                if (stream.Read(entry, 0, (int)entrySize) != entrySize)
                    break;

                // Check if empty
                bool empty = true;
                for (int j = 0; j < 16; j++) if (entry[j] != 0) empty = false;
                if (empty) break;

                string name = Encoding.Unicode.GetString(entry, 0x38, 72).TrimEnd('\0');
                if (string.IsNullOrEmpty(name)) break;

                ulong first = BitConverter.ToUInt64(entry, 0x20);
                ulong last = BitConverter.ToUInt64(entry, 0x28);

                parts.Add(new PartitionInfo { Name = name, FirstSector = first, LastSector = last });
            }

            return parts;
        }
    }
}