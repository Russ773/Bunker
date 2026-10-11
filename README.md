# Bunker

Windows Forms admin console for Rust game servers (WebRCON).

```
dotnet run --project src
```

## Releasing
Tag and push: `git tag v0.0.3 && git push origin v0.0.3`. GitHub Actions builds the Inno Setup
installer, publishes a GitHub Release (the app auto-updates from it) and uploads the installer
plus `latest.json` to the webspace.

To (re-)upload an existing release to the webspace: Actions -> "Upload to webspace" -> Run workflow -> tag.

Webspace repo secrets (FTP is used if `FTP_HOST` is set, otherwise SFTP):
- FTP: `FTP_HOST`, `FTP_USER`, `FTP_PASSWORD`, `FTP_PATH`, optional `FTP_PORT` (default 21). Uses explicit TLS when the server supports it.
- SFTP: `SFTP_HOST`, `SFTP_USER`, `SFTP_PATH`, and `SFTP_PASSWORD` or `SFTP_KEY`, optional `SFTP_PORT`.
