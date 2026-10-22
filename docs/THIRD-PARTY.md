# Third-party components

ImageRescue application source and its custom icon are MIT licensed; see LICENSE.

The self-contained Windows distribution includes the Microsoft .NET and Windows
Desktop runtimes. Their license and third-party notice files are provided in the `licenses/` folder
of the published payload (copied from the .NET SDK and the dotnet/wpf project). The application adds no third-party NuGet packages.

The installer is built with NSIS (https://nsis.sourceforge.io/). NSIS installer
stubs, Modern UI and their plug-ins have their respective licenses described at
https://nsis.sourceforge.io/License. This project distributes a generated installer,
not a modified NSIS compiler. Its installer source is provided in installer/.
