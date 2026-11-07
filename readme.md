# ffu-porter

Creates a fully ported Full Flash Update (FFU) file by combining a base device FFU with a port FFU.

## Description

This tool takes a **base FFU** (device-specific firmware containing correct bootloaders, modem, provisioning partitions, platform IDs, etc.) and a **port FFU** (containing the desired OS partitions such as MainOS, Data, etc.), and produces a fully ported FFU ready to flash on the target device.

It extracts the raw store images from both FFUs, merges partitions intelligently (keeping device-specific/provisioning partitions from the base, and OS partitions from the port when sizes match), then rebuilds a valid FFU using the base's metadata.

Supports V1 and V2 FFU formats, multiple stores, and compression (Windows-only for compressed).

## Usage

```
ffu-porter --base-ffu base.ffu --port-ffu port.ffu --ffu-file output.ffu [options]
```

### Required

- `--base-ffu` : Path to the base (device) FFU
- `--port-ffu` : Path to the port (OS source) FFU
- `--ffu-file` / `-f` : Path to the output ported FFU

### Optional

- `--port-partitions` : Comma-separated list of partitions to take from the port FFU (e.g. `MainOS,Data,EFIESP`). Default: all non-provisioning partitions.
- `--plat-id` / `-p` : Override platform ID(s) (semicolon separated)
- `--os-version` / `-o` : Override OS version
- `--anti-theft-version` / `-a` : Override anti-theft version
- `--block-size` / `-c` : Override block size
- `--sector-size` / `-s` : Override sector size
- `--full-flash-update-version` / `-v` : Override FFU version (V1 or V2)
- `--secure-boot-signing-command` / `-t` : Optional signing command
- `--keep-provisioning` : Always keep provisioning partitions from base (default: true)

### Example

```batch
ffu-porter ^
  --base-ffu "RM-1085_Base.ffu" ^
  --port-ffu "Generic_Windows10Mobile.ffu" ^
  --ffu-file "RM-1085_Ported.ffu" ^
  --port-partitions "MainOS,Data"
```

## Notes

- Partition sizes must match between base and port for a partition to be ported; otherwise the base version is kept.
- Provisioning partitions (modem, DPP, bootloaders, etc.) are kept from the base by default to avoid bricking.
- Always make a full device backup before flashing.
- For retail devices, unlock the UEFI first.
- Temp files are created in %TEMP% and cleaned up automatically.

## Building

Requires .NET 8 SDK.

```
dotnet build Img2Ffu.sln -c Release
```

Publish examples are in the publish_*.cmd scripts.

## Copyright

Based on img2ffu.

Copyright (c) 2019-2024, Gustave Monce - gus33000.me - @gus33000

*Portions from:*
Copyright (c) 2018, Rene Lergner - wpinternals.net - @Heathcliff74xda

This software is released under the MIT license.
