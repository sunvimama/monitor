# Monitor Wallpaper

A Windows utility for using an MP4 video on landscape monitors and a still image on portrait monitors.

## Run

Download and run `MonitorWallpaper.exe`. Choose the landscape video and portrait image, then select **Save and apply**. The app remembers the paths on that PC and can optionally apply them when you sign in to Windows.

The selected media files must remain available at their saved paths. This app is intended for a Windows setup with at least one landscape and one portrait display. The optional `Set-MonitorWallpapers.ps1` script sets still images by monitor orientation.

## Source

- `MonitorWallpaper.cs` is the C# source for the Windows app.
- `Set-MonitorWallpapers.ps1` is the PowerShell still-wallpaper utility.
- `MonitorWallpaper.exe` is a prebuilt Windows executable.

## License

See [LICENSE.md](LICENSE.md).
