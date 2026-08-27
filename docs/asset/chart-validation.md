# Chart Validation

The chart pipeline validates input at parsing, conversion, encryption, and self-test stages.

## SUS parsing

`SusParser` rejects malformed structural values such as invalid positions and records warnings for recoverable input problems, including malformed note tokens and unresolved BPM references.

By default, recoverable parser warnings are returned with the conversion result and printed to standard error.

With `--strict`, any parser warning causes conversion to stop before notes are generated.

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- chart text input.sus output.txt --strict
```

## Conversion checks

`SusToSiriusConverter` validates conditions that cannot be safely represented in the target chart format. Examples include:

- lane ranges outside the 12-lane play field;
- invalid SUS note types for supported channels;
- slide events without a group identifier;
- overlapping or mismatched split-line events;
- split-line starts without matching ends;
- overlapping slide groups;
- slide endings without a matching active slide;
- non-positive BPM values used for generated hold ticks.

These errors are fatal regardless of `--strict` because continuing would produce structurally invalid output.

## Timing behavior

Timing is calculated from the parsed BPM map. `#WAVEOFFSET` is added to generated timestamps by default.

Use:

```text
--ignore-wave-offset
```

to keep the generated time axis independent of the SUS wave offset.

Timing events parsed from `#TIL00` and split-line events parsed from `#TIL01` are emitted into the Sirius row stream together with playable notes.

## ENC validation

The ENC codec requires a key that encodes to exactly 32 UTF-8 bytes. Decode also rejects payloads that are too short or whose ciphertext length is not aligned to the AES block size.

Decoded plaintext is required to be valid UTF-8.

## Built-in crypto self-test

Run:

```powershell
dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -- chart selftest
```

The self-test checks two properties:

1. encoding is deterministic for identical input and key;
2. decode(encode(text)) returns the exact source text.

A successful run prints `selftest: ok` and exits with code `0`.
