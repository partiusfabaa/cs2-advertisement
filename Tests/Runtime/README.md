# Runtime checks

dotnet run --project Tests/Runtime/Runtime.Tests.csproj -c Release

Compiles the plugin sources with managed CounterStrikeSharp and Panorama test doubles. Covers HTML duration, reconnects, timer cleanup, config reloads, localization, welcome messages and Panorama restoration.

These checks do not exercise native entities or client rendering.
