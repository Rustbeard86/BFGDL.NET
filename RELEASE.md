# Release Guide

This document describes how to create releases for BFGDL.NET.

## Release Types

The project supports three build configurations for each platform:

1. **Framework-Dependent (FD)** - Requires .NET 10 Runtime, smallest size
2. **Self-Contained (SC)** - Includes runtime, single-file executable
3. **Native AOT** - Compiled to native code, best performance

## Supported Platforms

- Windows x64, ARM64
- Linux x64, ARM64
- macOS x64, ARM64 (Apple Silicon)

## Creating a Release

### Option 1: Tag-based Release (Recommended)

1. Create and push a version tag:
```bash
git tag v1.0.0
git push origin v1.0.0
```

2. GitHub Actions will automatically:
   - Build all 18 platform/configuration combinations
   - Create release archives (.zip for Windows, .tar.gz for Unix)
   - Create a GitHub Release with all artifacts
   - Generate release notes

### Option 2: Manual Workflow Trigger

1. Go to GitHub Actions tab
2. Select "Release" workflow
3. Click "Run workflow"
4. Enter version (e.g., v1.0.0)
5. Click "Run workflow"

## Build Matrix

The release workflow builds:

| Platform        | RID          | FD  | SC  | AOT |
|-----------------|--------------|-----|-----|-----|
| Windows x64     | win-x64      | ?  | ?  | ?  |
| Windows ARM64   | win-arm64    | ?  | ?  | ?  |
| Linux x64       | linux-x64    | ?  | ?  | ?  |
| Linux ARM64     | linux-arm64  | ?  | ?  | ?  |
| macOS x64       | osx-x64      | ?  | ?  | ?  |
| macOS ARM64     | osx-arm64    | ?  | ?  | ?  |

**Total: 18 artifacts per release**

## Artifact Naming Convention

Artifacts follow this pattern:
```
bfgdl-net-{platform}-{arch}-{type}-{version}.{ext}
```

Examples:
- `bfgdl-net-win-x64-aot-v1.0.0.zip`
- `bfgdl-net-linux-x64-sc-v1.0.0.tar.gz`
- `bfgdl-net-osx-arm64-fd-v1.0.0.tar.gz`

## Local Testing

To test builds locally before release:

### Framework-Dependent
```bash
dotnet publish -c Release -r win-x64 --no-self-contained
```

### Self-Contained
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true
```

### Native AOT
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishAot=true -p:StripSymbols=true
```

## File Sizes (Approximate)

| Build Type | Size | Notes |
|------------|------|-------|
| FD | ~200 KB | Requires .NET 10 Runtime |
| SC | 60-80 MB | Includes trimmed runtime |
| AOT | 10-15 MB | Native executable |

## Versioning

Follow [Semantic Versioning](https://semver.org/):
- **MAJOR**: Breaking changes
- **MINOR**: New features (backward compatible)
- **PATCH**: Bug fixes

Example: `v1.2.3`

## Pre-releases

For pre-release versions, append a suffix:
```bash
git tag v1.0.0-alpha.1
git push origin v1.0.0-alpha.1
```

Mark as pre-release in GitHub Release UI if needed.

## Troubleshooting

### Build fails for specific platform
- Check .NET 10 SDK availability for that RID
- Verify project supports that runtime identifier
- Check GitHub Actions logs for specific errors

### AOT build fails
- Ensure no reflection or dynamic code that breaks AOT
- Check for AOT warnings during local build
- Native AOT has some limitations (see .NET docs)

### Release not created
- Verify GitHub token permissions (needs `contents: write`)
- Check all build jobs completed successfully
- Review GitHub Actions logs

## Checklist Before Release

- [ ] All tests pass locally
- [ ] Version number updated in code (if shown to users)
- [ ] CHANGELOG.md updated (if exists)
- [ ] README.md is current
- [ ] No uncommitted changes
- [ ] Create version tag with `v` prefix
- [ ] Push tag to trigger release

## Post-Release

1. Verify all 18 artifacts uploaded correctly
2. Test download and extraction of key artifacts
3. Update documentation if needed
4. Announce release (if applicable)

## Notes

- First release should be `v1.0.0`
- GitHub Actions automatically creates release
- All artifacts are retained for 5 days during build
- Release artifacts are permanent once release is created
- Users can download any configuration they prefer
