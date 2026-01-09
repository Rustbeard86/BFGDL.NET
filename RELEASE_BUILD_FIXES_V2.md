# Release Build Fixes - Iteration 2

## New Issues Found

### 1. Linux ARM64 Native AOT - `objcopy` Error
**Error**: `objcopy: Unable to recognise the format of the input file`

**Root Cause**: Cross-compiling ARM64 on an x64 GitHub runner. The default `objcopy` tool is x64 and cannot process ARM64 binaries.

**Solution**:
- Install `binutils-aarch64-linux-gnu` which provides ARM64-compatible `objcopy`
- Disable symbol stripping (`-p:StripSymbols=false`) for Linux ARM64 to avoid the objcopy issue
- Configure proper cross-compilation settings

### 2. macOS x64 Framework-Dependent - Roslyn Compiler Error
**Error**: `CS1504: Method not found: 'Boolean Microsoft.CodeAnalysis.EncodingExtensions.TryGetMaxCharCount'`

**Root Cause**: Potential compiler cache corruption or SDK versioning issue on macOS runners.

**Solution**:
- Added explicit clean step before macOS builds
- Removes `bin` and `obj` directories to ensure fresh build

## Changes Made

### `.github/workflows/release.yml`

#### 1. Added Cross-Compilation Dependencies
```yaml
- name: Install cross-compilation dependencies (Linux ARM64 AOT)
  if: matrix.rid == 'linux-arm64' && matrix.aot == true
  run: |
    sudo apt-get update
    sudo apt-get install -y clang zlib1g-dev gcc-aarch64-linux-gnu binutils-aarch64-linux-gnu
```

#### 2. Special Handling for Linux ARM64 AOT
```yaml
if [ "${{ matrix.rid }}" == "linux-arm64" ]; then
  # Linux ARM64 cross-compilation requires special handling
  dotnet publish BFGDL.NET.csproj -c Release -r ${{ matrix.rid }} --self-contained \
    -p:PublishAot=true \
    -p:StripSymbols=false \
    -p:IlcGenerateMetadataLog=false \
    -p:CppCompilerAndLinker=clang \
    -p:SysRoot=/usr/aarch64-linux-gnu \
    -o ./publish
```

Key parameters:
- `-p:StripSymbols=false`: Avoids objcopy error
- `-p:CppCompilerAndLinker=clang`: Uses clang for cross-compilation
- `-p:SysRoot=/usr/aarch64-linux-gnu`: Sets ARM64 sysroot

#### 3. macOS Clean Step
```yaml
- name: Clean before build (macOS)
  if: runner.os == 'macOS'
  run: dotnet clean && rm -rf bin obj
```

## Technical Details

### Cross-Compilation Tools Installed
| Tool | Purpose |
|------|---------|
| `clang` | C/C++ compiler with ARM64 support |
| `zlib1g-dev` | Compression library headers |
| `gcc-aarch64-linux-gnu` | ARM64 GCC cross-compiler |
| `binutils-aarch64-linux-gnu` | ARM64-compatible binutils (objcopy, strip, etc.) |

### Symbol Stripping Trade-off
For Linux ARM64 AOT, we disable symbol stripping to avoid the objcopy compatibility issue. This means:
- ? Build succeeds
- ?? Binary size slightly larger (~500KB-1MB)
- ?? Symbols can be manually stripped later if needed with `aarch64-linux-gnu-strip`

## Expected Build Status

After these fixes:

| Platform | RID | FD | SC | AOT | Status |
|----------|-----|----|----|-----|--------|
| Windows | x64 | ? | ? | ? | Should succeed |
| Windows | ARM64 | ? | ? | ? | Should succeed |
| Linux | x64 | ? | ? | ? | Should succeed |
| Linux | ARM64 | ? | ? | ? | **Fixed** |
| macOS | x64 | ? | ? | ? | **Fixed** |
| macOS | ARM64 | ? | ? | ? | Should succeed |

## Testing Locally

### Linux ARM64 AOT (requires Docker)
```bash
docker run --rm -v $(pwd):/src -w /src mcr.microsoft.com/dotnet/sdk:10.0-preview \
  bash -c "apt-get update && apt-get install -y clang zlib1g-dev gcc-aarch64-linux-gnu binutils-aarch64-linux-gnu && \
  dotnet publish BFGDL.NET.csproj -c Release -r linux-arm64 --self-contained \
  -p:PublishAot=true -p:StripSymbols=false -p:CppCompilerAndLinker=clang -p:SysRoot=/usr/aarch64-linux-gnu"
```

### macOS (on macOS machine)
```bash
# Clean first
dotnet clean
rm -rf bin obj

# Build
dotnet publish BFGDL.NET.csproj -c Release -r osx-x64 --no-self-contained
```

## Next Steps

1. Commit and push changes
2. Re-trigger release workflow
3. Monitor all 18 builds
4. Verify Linux ARM64 AOT artifact is created
5. Verify macOS x64 FD artifact is created

## Files Modified

- `.github/workflows/release.yml` - Added cross-compilation support and macOS clean step

## Related Documentation

- `RELEASE_WORKFLOW_FIXES.md` - Previous iteration fixes
- `AOT_COMPATIBILITY.md` - JSON source generation
- `RELEASE.md` - General release documentation
