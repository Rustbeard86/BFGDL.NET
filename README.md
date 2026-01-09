# BFGDL.NET - Big Fish Games Downloader

A modern, cross-platform Big Fish Games downloader implemented in C# 14 with .NET 10.

## Features

- Multi-threaded Downloads - Concurrent segment downloads with configurable thread count
- Resume Support - Automatically resumes interrupted downloads
- Catalog Fetching - Fetch latest games directly from Big Fish Games catalog via GraphQL API
- Configuration File Support - Optional config.ini for default settings
- Professional CLI - Clean output with proper formatting
- SOLID Architecture - Built with dependency injection and clean interfaces
- Minimal Dependencies - Only Microsoft.Extensions
- C# 14 Features - Field keyword, collection expressions, primary constructors, generated regex
- Native AOT Support - Can be published as a native executable
- Cross-Platform - Runs on Windows, macOS, and Linux

## Requirements

- .NET 10 Runtime (or SDK for building from source)

## Installation

### From Source

```bash
git clone https://github.com/Rustbeard86/BFGDL.NET
cd BFGDL.NET
dotnet build -c Release
dotnet publish -c Release -r <runtime-identifier> --self-contained
```

The compiled executable will be in `bin/Release/net10.0/<runtime-identifier>/publish/`

**Common Runtime Identifiers:**
- Windows x64: `win-x64`
- Windows x86: `win-x86`
- Windows ARM64: `win-arm64`
- Linux x64: `linux-x64`
- Linux ARM64: `linux-arm64`
- macOS x64: `osx-x64`
- macOS ARM64 (Apple Silicon): `osx-arm64`

```ini
# Platform: win, mac
platform=win

# Language: eng, ger, spa, fre, ita, jap, dut, swe, dan, por
language=eng

```

For Native AOT:
```bash
dotnet publish -c Release -r <runtime-identifier> --self-contained -p:PublishAot=true
```

## Configuration

The application supports an optional `config.ini` file for default settings:

```ini
# Platform: win, mac
platform=win

# Language: eng, ger, spa, fre, ita, jap, dut, swe, dan, por
language=eng

# Generate script format (for future use)
gen_script=true

# Number of latest games to export when using --export-limit
export_limit=50
```

If no config file exists, the application uses sensible defaults (Windows, English, 50 games).

## Usage

### Command Line Options

| Option | Description |
|--------|-------------|
| `-h`, `--help` | Display help message |
| `-v`, `--version` | Display version information |
| `-d`, `--download` | Download games after fetching metadata |
| `-e`, `--extract` | Extract WrapIDs from installer files in current directory |
| `-j N`, `--jobs N` | Set number of concurrent downloads (default: 8) |
| `-c FILE`, `--config FILE` | Load configuration from FILE (default: config.ini) |
| `-p PLATFORM`, `--platform PLATFORM` | Set platform: win, mac (overrides config) |
| `-l LANG`, `--language LANG` | Set language: eng, ger, spa, fre, ita, jap, dut, swe, dan, por |
| `--export-installers-json FORMAT` | Export full (non-demo) installer segment lists grouped by WrapID language (FORMAT: pretty or min) |
| `--export-limit N` | Limit number of games exported (for testing) |

### Examples

**Export latest games metadata:**
```bash
# Export latest 50 games in pretty JSON
BFGDL.NET --export-installers-json=pretty

# Export specific number of games
BFGDL.NET --export-installers-json=min --export-limit=100

# Export with specific platform and language
BFGDL.NET --export-installers-json=pretty -p win -l eng --export-limit=25
```

**Download latest games:**
```bash
# Export and download latest 20 games
BFGDL.NET --export-installers-json=min --export-limit=20 -d

# With custom concurrency
BFGDL.NET --export-installers-json=min --export-limit=10 -d -j 16
```

**Fetch links for specific games:**
```bash
# Outputs to download-list.txt
BFGDL.NET F15533T1L2 F7028T1L1 F1T1L1
```

**Download specific games:**
```bash
# Download with default settings
BFGDL.NET -d F15533T1L2 F7028T1L1

# Download with 4 concurrent downloads
BFGDL.NET -d -j 4 F5260T1L1
```

**Extract from installers:**
```bash
# Scan current directory for installer files
BFGDL.NET -e

# Scan and download
BFGDL.NET -e -d
```

**Use custom config file:**
```bash
BFGDL.NET -c my-config.ini --export-installers-json=pretty --export-limit=30 -d
```

## How It Works

### Fetching WrapIDs

The application can fetch WrapIDs (game identifiers) in three ways:

1. **From Catalog** (`--export-installers-json`): Fetches latest games from Big Fish Games GraphQL API, sorted by release date
2. **From Installers** (`-e`): Scans local installer files to extract WrapIDs
3. **Direct Input**: Provide WrapIDs as command-line arguments

Priority order: Installers > Catalog > Direct Input

### Catalog Fetching

The `--export-installers-json` flag uses the Big Fish Games GraphQL API to:
1. Query the games catalog with filters for platform and language
2. Retrieve WrapIDs sorted by product list date (latest first)
3. Export metadata and download links for each game in JSON format

### Downloading Games

Once WrapIDs are obtained, the application:

1. Queries the Big Fish Games API for game metadata
2. Downloads all segments concurrently (configurable with `-j`)
3. Creates separate folders for each game
4. Supports resume for interrupted downloads
5. Filters out demo segments automatically

## Architecture

### SOLID Principles

The application follows SOLID principles:

- **Single Responsibility**: Each service has one clear purpose
- **Open/Closed**: Services implement interfaces for extensibility
- **Liskov Substitution**: Interface implementations are substitutable
- **Interface Segregation**: Focused, minimal interfaces
- **Dependency Inversion**: Dependencies injected via interfaces

### Project Structure

```
BFGDL.NET/
├── Configuration/
│   ├── ConfigurationLoader.cs    # Config file parser
├── Models/
│   ├── Configuration.cs          # Application configuration models
│   ├── GameInfo.cs               # Domain models for games and downloads
├── Services/
│   ├── InstallerWrapIdFetcher.cs # File-based WrapID fetcher
│   ├── BigFishCatalogClient.cs   # GraphQL catalog client
│   ├── IBigFishGamesClient.cs    # API client interface
│   ├── BigFishGamesClient.cs     # XML-RPC API client
│   ├── IDownloadService.cs       # Download service interface
│   ├── DownloadService.cs        # Multi-threaded downloader
│   ├── InstallerListExporter.cs  # JSON exporter for installers
│   ├── HtmlSanitizer.cs          # HTML entity decoder
├── Application.cs                # Main application orchestration
├── CommandLineOptions.cs          # CLI argument parser
├── Program.cs                    # Entry point with DI setup
└── config.ini                    # Optional configuration file
```

## Comparison with Original Python Implementation

### What's Different?

| Feature | Python Version | C# Version |
|---------|---------------|------------|
| Catalog Access | Playwright (~180MB) | GraphQL API (lightweight) |
| Downloads | aria2 (external) | Native .NET HttpClient |
| Configuration | config.ini only | config.ini + CLI overrides |
| Dependencies | playwright, beautifulsoup4, aria2, jq, xq | Microsoft.Extensions |
| Architecture | Script-based | SOLID with DI |
| Type Safety | Dynamic | Strongly typed with records |
| Performance | Good | Excellent (compiled, AOT-ready) |
| Platform | Cross-platform (with Cygwin/bash) | Cross-platform (native .NET) |

### Advantages

1. **Lightweight**: No heavy browser automation dependencies
2. **Type Safety**: Compile-time checking prevents many runtime errors
3. **Better Performance**: Compiled code with optimizations
4. **AOT Support**: Can be published as native executable
5. **Professional Output**: Clean, properly escaped output
6. **Flexible Configuration**: Config file + command-line overrides
7. **Testable**: Interface-based design enables easy unit testing
8. **Cross-Platform**: Native support for Windows, macOS, and Linux

## Supported Platforms and Languages

### Platforms
- Windows (`win`)
- macOS (`mac`)

### Languages
- English (`eng`)
- German (`ger`)
- Spanish (`spa`)
- French (`fre`)
- Italian (`ita`)
- Japanese (`jap`)
- Dutch (`dut`)
- Swedish (`swe`)
- Danish (`dan`)
- Portuguese (`por`)

## Troubleshooting

### Build Issues

If you encounter build errors:

```bash
# Clean and rebuild
dotnet clean
dotnet build -c Release
```

### Download Issues

- Ensure you have a stable internet connection
- Try reducing concurrent downloads with `-j 4`
- Check if the WrapID is valid
- Some games may have region restrictions

## Contributing

Contributions are welcome! Please ensure:

1. Code follows existing SOLID patterns
2. All changes are properly tested
3. XML documentation is added for public APIs
4. Commit messages are descriptive

## License

This project reimplements the functionality of bfg-dl in C# .NET 10.

## Acknowledgments

- Original bfg-dl Python implementation by com1100 and contributors
- bfg_wrapid_fetcher by kevinj93
