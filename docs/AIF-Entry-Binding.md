# The entry of an AIM and its binding to the Controller - this environment's choice

**Status.** This is the choice of OUR environment (the MPAI reference software in D:\AI), version 1. It is **not** part of the MPAI-AIF standard. The standard fixes only two things: the type of the entry of an Implementation (`module_t`, `typedef error_t *(module_t)()`) and the abstract functions an AIM calls (the `MPAI_AIFM_` functions). It fixes no binary binding. This document fixes one, so that an AIM can be written in any language that can follow plain C conventions (C++, Rust, C#), and so that a Controller written in another language could run the same AIM packages.

The reference implementation of the Controller side is `AIF\Controller\ModuleEntry.cs`; the AIM side of a worked example is `Test\ForeignAim\Entry.cs`. You do not need to read either to follow this document.

## 1. What the Controller does

1. It obtains the package of the AIM from the MPAI Store, checks its fingerprint with the Store, and unpacks it. Only after the Store has said the package is the one it approved is anything of it loaded.
2. It reads the AIM Metadata (L3): `Implementations` names the file (`BinaryName`), the `Architecture` and the `OperatingSystem`. It uses the Implementation for the machine it runs on. If there is none, or the file is of a kind it cannot host, the Module is refused with a stated reason.
3. It loads the file, finds the entry named `Module`, builds the table of functions of section 3, and calls the entry once with the address of that table.
4. From then on it calls the AIM's process function (section 4) for each exchange.

## 2. The entry

| Item | Value |
|---|---|
| Name | `Module` (exported symbol of a native library; public static method of a .NET assembly) |
| Shape | `int32_t Module(const MpaiApi *api)` |
| Argument | the address of the table of functions (section 3). It stays valid for the whole life of the AIM instance. |
| Return | an error code (section 6). 0 means the AIM started. |
| Calls it makes | only the functions of the table, and only to register its process function and to ask for settings and Port keys (it may emit nothing here: there is no call yet) |
| Standard shape | `module_t` takes nothing and returns an error pointer. This environment passes the table as the one argument and returns an integer. That difference is our choice. |

An entry that takes nothing, as in the standard, is also started: but it has no table, so it can never be given data, and the Controller says so if it is asked to process anything.

For a .NET Implementation: a public static method `int Module(IntPtr api)` in the assembly named by `BinaryName`; the functions are called through the pointers with the C calling convention. For a native Implementation (not built in this environment yet): an exported function with the C calling convention.

## 3. The table of functions given to the AIM (`MpaiApi`, version 1)

All integers are little-endian. Every pointer field is 8 bytes (64-bit machine). The table is 56 bytes. A later version may be larger: the AIM reads `size` and `version` first and uses only what it knows.

| Offset | Field | Type | Meaning |
|---|---|---|---|
| 0 | `size` | uint32 | bytes in the table (56 in version 1) |
| 4 | `version` | uint32 | 1 |
| 8 | `register_process` | function | give the Controller the AIM's process function |
| 16 | `setting` | function | read a setting of the AIM |
| 24 | `port_count` | function | how many Ports |
| 32 | `port_key` | function | the key of one Port |
| 40 | `emit` | function | give a datum at an Output Port |
| 48 | `set_reason` | function | say why a call failed |

Every function returns an int32 error code (section 6) and uses the C calling convention of the platform.

| Function | Inputs | Outputs | Notes |
|---|---|---|---|
| `register_process` | `void *user`, `MpaiProcessFn fn` | none | Called from the entry. `user` is given back unchanged on every call of `fn`. One process function per AIM; registering again replaces it. |
| `setting` | `const uint8_t *name`, `int32 name_len`, `uint8_t *buf`, `int32 cap`, `int32 *needed` | the value in `buf` (UTF-8, no end marker); its length in `*needed` | Returns NOT_FOUND if the AIM has no such setting. If `cap` is too small, returns BUFFER_TOO_SMALL and sets `*needed`: ask again with a larger buffer. |
| `port_count` | `int32 direction` (0 = Input, 1 = Output), `int32 *count` | the number of Ports of that direction in `*count` | |
| `port_key` | `int32 direction`, `int32 index` (from 0), `uint8_t *buf`, `int32 cap`, `int32 *needed` | the key in `buf` (UTF-8), its length in `*needed` | Same buffer rule as `setting`. |
| `emit` | `void *call`, `const uint8_t *key`, `int32 key_len`, `const uint8_t *data`, `int32 data_len` | none | Gives one datum at the Output Port named by `key`. Only during a call of the process function, with the `call` it was given. The Controller copies the bytes before `emit` returns. Emitting twice at the same key in one call: the last one is kept. |
| `set_reason` | `void *call`, `const uint8_t *text`, `int32 text_len` | none | A short human-readable reason (UTF-8) for a failure. The Controller puts it in its answer. Only during a call. |

**The process function** (the type `MpaiProcessFn`, the AIM's, registered with `register_process`):

`int32 fn(void *user, void *call, const MpaiDatum *inputs, int32 input_count)`

It is called once for each exchange: the Controller gives it what the Input Ports hold, the AIM emits what its Output Ports give, and returns 0, or an error code (and, if it wishes, a reason with `set_reason`). A non-zero return makes the exchange fail; the Controller reports the code and the reason.

## 4. Data layout

**A key** is the text `DataType#PortNumber` in UTF-8, for example `AIH-ADT-V1.1#1`: the Data Type of the Port (as the AIM Metadata names it) and its Port Number (1 for the first Port of that Data Type and direction, 2 for the second, and so on). The keys of an AIM are the ones `port_key` lists; do not build them by hand.

**A datum** is the bytes of the Data Object at that Port. In this environment each Data Type that has a JSON schema is carried as its JSON instance in UTF-8, with no end marker. A Data Type that is binary (speech, pictures) is carried as its bytes as the Data Type defines them.

**`MpaiDatum`** (24 bytes, fixed layout):

| Offset | Field | Type |
|---|---|---|
| 0 | `key` | pointer to the key bytes |
| 8 | `data` | pointer to the datum bytes |
| 16 | `key_len` | int32, bytes in the key |
| 20 | `data_len` | int32, bytes in the datum (up to 2 GiB minus 1) |

`inputs` is an array of `input_count` of them. A Port that holds nothing in this exchange is simply not in the array.

**Text** everywhere (names, keys, settings, reasons) is UTF-8, counted by its length. It has no terminating zero and may not be assumed to have one.

## 5. Who owns memory, and threads

| Question | Answer |
|---|---|
| Who owns `inputs`, and the keys and data it points to? | The Controller. They are valid until the process function returns. The AIM must copy what it keeps. |
| Who owns the bytes given to `emit` and `set_reason`? | The AIM. The Controller copies them before the call returns; the AIM may free or reuse them afterwards. |
| Who owns the buffers given to `setting` and `port_key`? | The AIM (it provides them; the Controller only writes into them, up to `cap`). |
| Who owns the table? | The Controller. It stays valid for the life of the AIM instance; the AIM must not free it. |
| Which calls may the AIM make, and when? | `register_process`, `setting`, `port_count` and `port_key` from the entry or from the process function; `emit` and `set_reason` only from the process function, with the `call` it was given. `emit` outside a call returns NOT_IN_CALL. |
| What does `call` mean? | An opaque value. Do not interpret it; pass it back to `emit` and `set_reason`. |
| Threads | The Controller calls the entry once. It calls the process function of one AIM instance one exchange at a time (never two at once), but not always from the same thread. The AIM must not keep thread-bound state. Table functions may be called only while the entry or the process function is running; the AIM may not call them from threads of its own. |
| Exceptions | None may cross the boundary, in either direction. A language that has them catches them inside the AIM and returns an error code. |

## 6. Error codes (int32)

| Code | Name | Meaning |
|---|---|---|
| 0 | OK | success |
| 1 | FAILED | the function could not do what it was asked |
| 2 | BAD_ARGUMENT | a pointer that must not be empty is, a direction or index out of range, a negative length |
| 3 | BUFFER_TOO_SMALL | the buffer cannot hold the answer; `*needed` says how much is needed |
| 4 | NOT_FOUND | no such setting |
| 5 | NOT_IN_CALL | `emit` or `set_reason` was called outside a call of the process function |
| 6 to 99 | reserved | for later versions of this binding |
| 100 and above | the AIM's own | any code the AIM returns from its entry or its process function; the Controller reports the number and the reason given with `set_reason` |

## 7. What is deliberately not here

- No call from the AIM to other AIMs, to the Storage or to the Trust functions: those are the Controller's. When an AIM needs them, they are added to the table in a later version, with a larger `size`.
- No streaming of a datum in pieces, and no asynchronous callbacks: one exchange is one call, with all its inputs and all its outputs.
- No native or Python host in the reference Controller yet: an Implementation of those kinds is refused with a reason ("... is not a .NET assembly ..."). The binding is written so that one can be added.

## 8. A short worked example in words

The tiny test AIM `Foreign.Aim` (in `Test\ForeignAim`) does this: its entry reads the table, registers its process function, and returns 0. For each exchange its process function asks `port_key` for the key of the first Input Port and of the first Output Port, looks for the datum with the first key among the inputs, asks `setting` for "Greeting", emits at the first Output Port the text `{"echo": <what it was given>, "setting": "<greeting>"}`, and returns 0. If nothing arrived at the Input Port it calls `set_reason` with "nothing at the Input Port ..." and returns 7. The Controller answers the exchange with that output, or with that code and reason.
