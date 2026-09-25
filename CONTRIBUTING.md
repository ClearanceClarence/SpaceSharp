# Contributing to SpaceSharp

Thanks for your interest. SpaceSharp is a small project, so the process is light.

## Ways to help

- **Report bugs** and **suggest features** through the [issue forms](https://github.com/ClearanceClarence/SpaceSharp/issues/new/choose). Clear steps and a screenshot go a long way.
- **Add a palette.** Palettes are a name and a list of colors in `SpaceSharp/Util/Palette.cs`; see the Retro entry for the simplest form. Check it in both color modes and both themes.
- **Add file types.** Extensions and their categories live in `BuildExtensionMap()` in the same file.
- **Improve the docs.** The README and CHANGELOG are plain Markdown; fixes to wording or missing steps are welcome.
- **Code.** Anything from the [ideas list](README.md#ideas-for-the-future) or an open issue. For larger changes, open an issue first so we can agree on the approach before you spend time on it.

## Setting up

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows 10 or 11.
2. Clone the repository and open `SpaceSharp.sln` in JetBrains Rider or Visual Studio, or run `dotnet run --project .\SpaceSharp\SpaceSharp.csproj`.
3. Velopack is the only NuGet dependency and restores automatically.

## Pull requests

- Branch from `main`, one change per pull request.
- Keep the existing style: file-scoped namespaces, `var` where the type is obvious, XML doc comments on public members, and the formatting in `.editorconfig`.
- Use American English in code, comments and UI text.
- Every UI string a user can see should follow the tone of the rest of the app: short, plain, no jargon.
- New settings go in `AppSettings`, `SettingsWindow` and `MainWindow.ApplySettings()`, and get a line in the README's settings table.
- New shortcuts go in `MainWindow.Window_PreviewKeyDown`, the About window's shortcut list and the README's shortcut table.
- Add a line to `CHANGELOG.md` under an "Unreleased" heading describing the change from the user's point of view.
- Test on a real drive, not only a small folder. Performance problems show up at a few hundred thousand files.

## Releasing (maintainer)

1. Bump `<Version>` in `SpaceSharp/SpaceSharp.csproj` and move the "Unreleased" changelog entries under the new version.
2. Publish the portable exe and the Velopack packages as described in the README.
3. Tag the commit with the version number and create the GitHub release with the changelog section as its notes.
