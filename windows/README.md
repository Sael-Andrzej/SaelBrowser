# SaelBrowser dla Windows

Natywna aplikacja WPF dla Windows 10/11 używająca Microsoft Edge WebView2.
Android pozostaje niezależnym, działającym modułem projektu.

## Budowanie i testy

```powershell
dotnet test tests/SaelBrowser.Core.Tests/SaelBrowser.Core.Tests.csproj -c Release
dotnet build src/SaelBrowser.Windows/SaelBrowser.Windows.csproj -c Release
dotnet publish src/SaelBrowser.Windows/SaelBrowser.Windows.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
dotnet build installer/SaelBrowser.Installer/SaelBrowser.Installer.wixproj -c Release
```

Instalator jest generowany w `artifacts/installer`. Aplikacja jest publikowana jako
samodzielna dla `win-x64`; wymaga aktualnego Microsoft Edge WebView2 Evergreen Runtime.
Jeżeli runtime nie jest dostępny, aplikacja wyświetla jednoznaczny komunikat zamiast
próbować użyć innego lub nieweryfikowanego silnika.

## Zasady bezpieczeństwa

- treść stron jest niezaufanym wejściem,
- kod strony nie otrzymuje obiektów hosta .NET ani kanału web messages,
- backend evidence wymaga HTTPS i ma produkcyjny fallback,
- brak niezależnego dowodu zawsze prowadzi do `UNKNOWN`,
- clickbait nigdy nie jest automatycznie traktowany jako fałsz,
- analiza jest anulowana po każdej nawigacji.
