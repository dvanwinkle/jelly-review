# Contributing to JellyReview

Thanks for your interest in contributing! Here's how to get started.

## Development Setup

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download)
- A running [Jellyfin](https://jellyfin.org) server (10.10+) for testing

### Building

```bash
cd Jellyfin.Plugin.JellyReview
dotnet restore
dotnet build --configuration Release
```

### Installing Locally

```bash
dotnet publish --configuration Release --output ../publish
```

Copy the contents of `publish/` into your Jellyfin plugins directory (e.g. `config/plugins/JellyReview/`) and restart Jellyfin.

## Submitting Changes

1. Fork the repository and create a branch from `main`.
2. Make your changes.
3. Test the plugin in a running Jellyfin instance.
4. Open a pull request with a clear description of what you changed and why.

## Release Metadata

- Every code change must update `CHANGELOG.md` under `## [Unreleased]`.
- When creating a release tag, convert the unreleased changelog notes into the tagged version section.
- Keep `Directory.Build.props` and `build.yaml` in sync with the released plugin version and metadata.

## Reporting Issues

- Use the **Bug Report** template for bugs.
- Use the **Feature Request** template for ideas.
- Include your Jellyfin version, OS, and steps to reproduce if applicable.

## Code Style

- Follow existing conventions in the codebase.
- An `.editorconfig` is included — most editors will pick it up automatically.
- Keep changes focused. One feature or fix per PR.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
