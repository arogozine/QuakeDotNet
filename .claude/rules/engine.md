---
paths:
  - Quake.Engine/**/*.cs
---

# Engine Rules (Quake.Engine)

- `Helpers/` is for port-specific / .NET-specific code. No copyright header in these files.
- Structs and enums go in `Models/`.
- Convert groups of related constants (`#define` enumerations) into enums where appropriate.
- Use u8 strings (`"..."u8`) where reasonable. .NET APIs, IO, and exception messages are exempt.
