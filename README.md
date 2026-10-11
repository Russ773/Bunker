# Chaos Admin

Windows Forms admin console for Rust game servers (WebRCON).

```
dotnet run --project src
```

## Releasing
Tag and push: `git tag v0.0.2 && git push origin v0.0.2`. GitHub Actions builds the Inno Setup
installer, publishes a GitHub Release (the app auto-updates from it) and uploads the installer
plus `latest.json` to the webspace over SFTP.

Webspace repo secrets: `SFTP_HOST`, `SFTP_USER`, `SFTP_PATH`, and either `SFTP_PASSWORD` or `SFTP_KEY`
(optional `SFTP_PORT`). If `SFTP_HOST` is unset the upload step is skipped.
