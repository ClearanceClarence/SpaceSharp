# Security

## Reporting a vulnerability

If you find a security problem in SpaceSharp, please don't open a public issue. Use GitHub's private reporting instead: go to the repository's **Security** tab and choose **Report a vulnerability**. You'll get a reply within a week.

## What SpaceSharp does and doesn't do

- It reads file and folder metadata on the drives you choose to scan. It never reads file contents.
- It deletes only what you explicitly select and confirm, and only to the Recycle Bin.
- It writes one settings file to `%AppData%\SpaceSharp\settings.json`.
- Installed copies contact `api.github.com` to check for new releases (this can be turned off in Settings → Updates). The portable exe makes no network requests.
- It has no telemetry and collects no data.

## Supported versions

Only the latest release receives fixes. Please update before reporting.
