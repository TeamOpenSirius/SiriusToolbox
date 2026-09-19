# MasterTool Architecture

## Data-model source

`lib/Sirius.Protocol.dll` is the authoritative MasterMemory model assembly. It contains generated `MemoryDatabase`, `DatabaseBuilder`, table types, `[MemoryTable]` metadata, primary indexes, and MessagePack fields.

The Toolbox intentionally does not duplicate those generated models.

## Synchronization

```text
Environment -> authentication -> login -> master manifest -> mastermemory.db
                                                |
                                                +-> Sirius.Protocol MemoryDatabase
                                                +-> typed per-table JSON
```

## Database edits

```text
mastermemory.db
  -> MemoryDatabase loads all generated tables
  -> locate table through MetaDatabase
  -> locate/construct typed row
  -> apply typed JSON/CLI values through reflection
  -> DatabaseBuilder rebuilds only the changed table
  -> original untouched table blocks are copied byte-for-byte
  -> full rebuilt DB is loaded again through MemoryDatabase
  -> validated output is written
```

This keeps table sorting, MessagePack formatting, and MasterMemory encoding under the generated MasterMemory implementation rather than a hand-written serializer.

For `--in-place`, the CLI creates `<input>.bak`, writes to a temporary file, validates it, and only then replaces the input.
