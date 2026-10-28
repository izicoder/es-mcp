# Third-party files

## Everything64.dll

- **What:** the 64-bit SDK client library for [Everything](https://www.voidtools.com/), by voidtools.
- **Why it is here:** it ships next to `es-mcp.exe` so the server works as soon as it is installed.
  Without it the server starts but every search fails with a missing-library error.
- **Source:** `Everything-SDK.zip` from <https://www.voidtools.com/support/everything/sdk/>.
- **Version:** the SDK build distributed at the time of writing. The DLL carries no version resource.
- **SHA256:** `81B5BE18126ACD2C2B913F8F4A821E476B18393CDD3DEBD03387C50AFD8DB88F`
- **Licence:** the SDK zip ships no licence file. The DLL is redistributed here unmodified, in the
  spirit of the SDK's purpose (writing clients for Everything), with credit to voidtools. If
  voidtools objects, delete this file, restore the `Everything64.dll` rule in `.gitignore`, and
  users can supply their own copy through `EVERYTHING_SDK_PATH`.

Nothing else in this repository is third-party code.
