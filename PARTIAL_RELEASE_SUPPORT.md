# Partial Release Support

## Overview

Modified the release workflow to create releases even when some build configurations fail. This ensures users can download available builds while problematic platforms are being fixed.

## Changes Made

### 1. Continue on Build Errors
```yaml
- name: Build and Publish
  shell: bash
  continue-on-error: true
  id: build
```

**Benefit**: Failed builds don't stop the entire workflow.

### 2. Conditional Artifact Creation
```yaml
- name: Create archive (Windows)
  if: runner.os == 'Windows' && steps.build.outcome == 'success'
  
- name: Upload artifact
  if: steps.build.outcome == 'success'
```

**Benefit**: Only create and upload artifacts for successful builds.

### 3. Always Create Release
```yaml
create-release:
  name: Create GitHub Release
  needs: build-and-release
  if: always()
```

**Benefit**: Release is created even if some build jobs fail.

### 4. Build Status in Release Notes
```yaml
? **X of 18 build configurations completed successfully**

### Available Downloads
- `bfgdl-net-win-x64-aot-v1.0.0.zip` (12.5M)
- `bfgdl-net-linux-x64-sc-v1.0.0.tar.gz` (7.5M)
...
```

**Benefit**: Users can see exactly what's available and what succeeded.

### 5. Fail on Unmatched Files = False
```yaml
fail_on_unmatched_files: false
```

**Benefit**: Release creation doesn't fail if some artifacts are missing.

## Workflow Behavior

### Before (fail-fast: true, no continue-on-error)
```
Build 1 fails ? All other builds canceled ? No release created ?
```

### Now (fail-fast: false, continue-on-error: true, if: always())
```
Build 1 fails ? Other builds continue ? Release created with available artifacts ?
```

## Example Release Scenario

If Linux ARM64 AOT fails but all others succeed:

**Release will include:**
- ? 17 successful build artifacts
- ? Full release notes with build status
- ? List of available downloads with sizes
- ? Note about potentially missing builds

**Users will see:**
```markdown
## Build Status
? **17 of 18 build configurations completed successfully**

### Available Downloads
- bfgdl-net-win-x64-fd-v1.0.0.zip (592 KB)
- bfgdl-net-win-x64-sc-v1.0.0.zip (65 MB)
- bfgdl-net-win-x64-aot-v1.0.0.zip (12 MB)
...
(Linux ARM64 AOT not listed - build failed)
```

## Benefits

1. **Faster Releases**: Don't wait to fix all platforms before releasing
2. **User Accessibility**: Users on working platforms can download immediately
3. **Progressive Improvement**: Fix problematic platforms in future releases
4. **Transparency**: Users see exactly what succeeded
5. **No Manual Work**: Automatic release creation with whatever succeeded

## Testing Locally

To test continue-on-error behavior:

```yaml
# Simulate failure
- name: Build and Publish
  continue-on-error: true
  id: build
  run: |
    if [ "${{ matrix.rid }}" == "linux-arm64" ]; then
      exit 1  # Fail this build
    fi
    # Normal build...
```

## Known Failing Builds

Currently, these builds may fail:
- ?? Linux ARM64 Native AOT - Cross-compilation library issues
- ?? macOS x64 Framework-Dependent - Roslyn compiler issues (intermittent)

These will be automatically excluded from the release, but won't prevent release creation.

## Future Improvements

1. Add GitHub Actions workflow summary showing which builds succeeded/failed
2. Create separate issues for failing builds automatically
3. Add retry logic for transient failures
4. Cache cross-compilation dependencies to speed up ARM64 builds

## Files Modified

- `.github/workflows/release.yml` - Added partial release support

## Related Documentation

- `RELEASE_BUILD_FIXES_V2.md` - Previous build fixes
- `AOT_COMPATIBILITY.md` - JSON source generation
- `RELEASE.md` - General release documentation
