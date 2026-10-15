# ServiceList.csproj — small cleanups

- Bump `<Version>1.0.3</Version>` → `<Version>1.1.0</Version>` (and the `VERSION` file).
- Remove the duplicated `<Description>` element (the file has it twice; keep the long one).
- Package versions stay as they are. Do NOT bump System.CommandLine: the code targets
  2.0.0-beta4.22272.1 (`AddAlias`, `SetHandler(InvocationContext)`, `HasOption`).
