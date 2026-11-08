# ffu-porter

Creates a fully ported Full Flash Update (FFU) file by combining a base device FFU with a port FFU, with optional injection of a compiled shell or files into MainOS.

## Description

This tool takes a **base FFU** (device-specific firmware) and a **port FFU** (source of the OS/partitions to port) and produces a fully ported FFU.

**Full auto-detection**: You only need to supply the two FFU files. The tool automatically detects and reuses every size, platform ID, OS version, anti-theft version, block size, sector size, device path, FFU version, and other metadata from the base FFU.

It creates a **sparse temporary image** and only writes the real data blocks from the base FFU, then streams the needed partitions from the port FFU into it. On Windows this keeps disk usage close to the actual data size of the FFUs instead of the full logical disk size.

You can also **inject** a compiled shell (or any files) directly into the MainOS NTFS volume before the final FFU is written.

Supports V1 and V2 FFU formats, multiple stores, and compression (Windows-only for compressed).

## Usage

```
ffu-porter --base-ffu base.ffu --port-ffu port.ffu --ffu-file output.ffu [options]
```

### Required (that's all you need)

- `--base-ffu` : Path to the base (device) FFU
- `--port-ffu` : Path to the port (OS source) FFU
- `--ffu-file` / `-f` : Path to the output ported FFU

### Optional

- `--port-partitions` : Comma-separated list of partitions to take from the port FFU (e.g. `MainOS,Data`). Default: all non-provisioning partitions.
- `--keep-provisioning` : Always keep provisioning partitions from base (default: true)
- `--temp-dir` : Directory for temporary image files. Defaults to system temp.
- `--inject` : Path to a directory or file to inject into MainOS (compiled shell, boot animation, etc.).
- `--inject-target` : Target path inside MainOS. Default: `\Windows\SystemApps\Windows12MobileShell`
- `--secure-boot-signing-command` / `-t` : Optional signing command

### Example (Windows 12 Mobile shell injection)

```batch
ffu-porter ^
  --base-ffu "RM-1085_Base.ffu" ^
  --port-ffu "Generic_Windows10Mobile.ffu" ^
  --ffu-file "result_win12_ffu.ffu" ^
  --inject "C:\Build\Windows12MobileShell\ARM\Release" ^
  --inject-target "\Windows\SystemApps\Windows12MobileShell" ^
  --temp-dir "D:\Temp"
```

## Notes

- All sizes, platform IDs, OS version, device paths, etc. are auto-detected from the base FFU.
- Temporary files are sparse on Windows: a 3–4 GB FFU no longer expands to hundreds of GB of real disk usage.
- The DPP partition is blanked before FFU generation and blank blocks are omitted, preventing Thor2 `DPP_PARTITION_HAS_DATA_IN_FFU` (0x220002) errors without breaking block alignment.
- Primary bootloader partitions (SBL1/2/3, TZ, RPM, QSEE, HYP, etc.) and their BACKUP_ counterparts are always kept from the base FFU to reduce RKH mismatches.
- If Thor2 still reports `DEV_RKH_MISMATCH_ERROR` (0x00030007), flash with the skip flag:
  ```
  thor2 -mode uefiflash -ffufile result_win12_ffu.ffu -skip_rkh_check
  ```
- `--inject` mounts the MainOS NTFS volume (via DiscUtils) and copies the given files into the target path.
- Partition sizes must match between base and port for a partition to be ported; otherwise the base version is kept.
- Other provisioning partitions (modem, bootloaders, etc.) are kept from the base by default to avoid bricking.
- Always make a full device backup before flashing.
- For retail devices, unlock the UEFI first.
- Temp files are cleaned up automatically.

## Building

Requires .NET 8 SDK.

```
dotnet build Img2Ffu.sln -c Release
dotnet publish Img2Ffu/Img2Ffu.csproj -c Release -r win-x64 --self-contained
```

GitHub Actions automatically builds and publishes binaries for Windows, Linux, and macOS (x64/arm64).

## Copyright

Based on img2ffu.

Copyright (c) 2019-2024, Gustave Monce - gus33000.me - @gus33000

*Portions from:*
Copyright (c) 2018, Rene Lergner - wpinternals.net - @Heathcliff74xda

This software is released under the MIT license.
