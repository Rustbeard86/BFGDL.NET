# BFGDL.NET - Big Fish Games Downloader

A modern, cross-platform Big Fish Games downloader implemented in C# 14 with .NET 10.

## Features

- ? **WebView2 Integration** - Uses embedded Chromium for JavaScript execution
- ? **Multi-threaded Downloads** - Concurrent segment downloads with configurable thread count
- ? **Resume Support** - Automatically resumes interrupted downloads
- ? **Web Fetching** - Fetch latest games directly from Big Fish Games website
- ? **Configuration File Support** - Optional config.ini for default settings
- ? **Professional CLI** - Clean, properly escaped output using Spectre.Console
- ? **SOLID Architecture** - Built with dependency injection and clean interfaces
- ? **Minimal Dependencies** - Only Microsoft.Extensions, Spectre.Console, and WebView2
- ? **C# 14 Features** - Field keyword, collection expressions, primary constructors, generated regex

## Requirements

- .NET 10 Runtime (or SDK for building from source)
- **For web fetching (-w flag)**: WebView2 Runtime (built into Windows 10/11)
- **Windows OS**: WebView2 requires Windows for COM interop

### Important Notes

?? **Native AOT Not Supported**: Due to WebView2's COM interop requirements, Native AOT compilation is disabled. The application uses standard .NET JIT compilation for full WebView2 compatibility.

?? **STA Threading**: The application uses `[STAThread]` attribute for WebView2 COM interop. This is automatically handled and requires no user action.

## Installation

### From Source

```bash
git clone <repository-url>
cd BFGDL.NET
dotnet build -c Release
dotnet publish -c Release
```

The compiled executable will be in `bin/Release/net10.0/publish/`

### WebView2 Runtime

WebView2 is built into Windows 10/11. If needed, download from:
https://developer.microsoft.com/en-us/microsoft-edge/webview2/

## Configuration

The application supports an optional `config.ini` file for default settings:

```ini
# Platform: win, mac
platform=win

# Language: eng, ger, spa, fre, ita, jap, dut, swe, dan, por
language=eng

# Generate script format (for future use)
gen_script=true

# Number of latest games to fetch when using -w/--web flag
latest_games_count=50
```

If no config file exists, the application uses sensible defaults (Windows, English, 50 games).

## Usage

### Command Line Options

| Option | Description |
|--------|-------------|
| `-h`, `--help` | Display help message |
| `-v`, `--version` | Display version information |
| `-e`, `--extract` | Extract WrapIDs from installer files in current directory |
| `-w`, `--web [N]` | Fetch latest N games from website (uses WebView2) |
| `-d`, `--download` | Download games after fetching metadata |
| `-j N`, `--jobs N` | Set number of concurrent downloads (default: 8) |
| `-c FILE`, `--config FILE` | Load configuration from FILE (default: config.ini) |
| `-p PLATFORM`, `--platform PLATFORM` | Set platform: win, mac (overrides config) |
| `-l LANG`, `--language LANG` | Set language: eng, ger, spa, fre, ita, jap, dut, swe, dan, por |

### Examples

**Fetch latest games from website:**
```bash
# Uses config.ini (or defaults to 50 games)
BFGDL.NET -w

# Fetch specific number of games
BFGDL.NET -w 100

# Fetch with specific platform and language
BFGDL.NET -w 25 -p win -l eng
```

**Download latest games:**
```bash
# Fetch and download latest 20 games
BFGDL.NET -w 20 -d

# With custom concurrency
BFGDL.NET -w 10 -d -j 16
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
BFGDL.NET -c my-config.ini -w 30 -d
```

## How It Works

### Fetching WrapIDs

The application can fetch WrapIDs (game identifiers) in three ways:

1. **From Web** (`-w`): Uses WebView2 (embedded Chromium) to execute JavaScript and scrape the Big Fish Games website
2. **From Installers** (`-e`): Scans local installer files to extract WrapIDs
3. **Direct Input**: Provide WrapIDs as command-line arguments

Priority order: Installers ? Web ? Direct Input

### Web Fetching with WebView2

The `-w` flag uses Microsoft's WebView2 to:
1. Launch a headless Chromium browser
2. Navigate to Big Fish Games website
3. Wait for JavaScript/React to render content
4. Extract WrapIDs from the rendered HTML
5. Filter by platform and language

**Why WebView2?**
- Big Fish Games uses a React SPA that requires JavaScript execution
- WebView2 is lightweight (~2MB) and uses Windows' built-in Edge runtime
- No external browser download needed (unlike Playwright)
- Native Microsoft solution with excellent performance

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
??? Configuration/
?   ??? ConfigurationLoader.cs    # Config file parser
??? Models/
?   ??? Configuration.cs          # Application configuration models
?   ??? GameInfo.cs              # Domain models for games and downloads
??? Services/
?   ??? IWrapIdFetcher.cs        # WrapID fetching interface
?   ??? WebView2WrapIdFetcher.cs # WebView2-based web fetcher
?   ??? InstallerWrapIdFetcher.cs # File-based WrapID fetcher
?   ??? IBigFishGamesClient.cs    # API client interface
?   ??? BigFishGamesClient.cs     # XML-RPC API client
?   ??? IDownloadService.cs       # Download service interface
?   ??? DownloadService.cs        # Multi-threaded downloader
?   ??? IConsoleOutput.cs         # Console output abstraction
?   ??? HtmlSanitizer.cs          # HTML entity decoder
?   ??? ProgressColumns.cs        # Custom Spectre.Console columns
??? Application.cs               # Main application orchestration
??? CommandLineOptions.cs        # CLI argument parser
??? Program.cs                   # Entry point with DI setup
??? config.ini                   # Optional configuration file
```

## Comparison with Original Python Implementation

### What's Different?

| Feature | Python Version | C# Version |
|---------|---------------|------------|
| Web Scraping | Playwright (~180MB) | WebView2 (~2MB, built-in) |
| Downloads | aria2 (external) | Native .NET HttpClient |
| Configuration | config.ini only | config.ini + CLI overrides |
| Dependencies | playwright, beautifulsoup4, aria2, jq, xq | Microsoft.Extensions, Spectre.Console, WebView2 |
| Architecture | Script-based | SOLID with DI |
| Type Safety | Dynamic | Strongly typed with records |
| Performance | Good | Excellent (compiled, AOT-ready) |
| Platform | Cross-platform (with Cygwin/bash) | Windows-native (WebView2 requirement) |

### Advantages

1. **Lightweight**: Uses Windows' built-in Edge WebView2 runtime
2. **Type Safety**: Compile-time checking prevents many runtime errors
3. **Better Performance**: Compiled code with optimizations
4. **AOT Support**: Can be published as native executable
5. **Professional Output**: Spectre.Console provides clean, properly escaped output
6. **Flexible Configuration**: Config file + command-line overrides
7. **Testable**: Interface-based design enables easy unit testing
8. **Windows-Native**: No Cygwin/bash dependencies needed

## Supported Platforms and Languages

### Platforms
- Windows (`win`) - **Primary target with WebView2**
- macOS (`mac`) - Supported for downloads, web fetching requires Playwright alternative

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

### WebView2 Issues

If web fetching fails:
- Ensure WebView2 Runtime is installed (built into Windows 10/11)
- Check Windows version (WebView2 requires Windows 10 1803 or later)
- Download standalone runtime: https://developer.microsoft.com/en-us/microsoft-edge/webview2/

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
- Spectre.Console library for beautiful CLI output
- Microsoft WebView2 for embedded browser functionality
