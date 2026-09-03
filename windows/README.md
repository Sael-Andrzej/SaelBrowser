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

## Niepodpisana wersja beta

Obecne pliki `SaelBrowser.exe` i MSI nie mają podpisu Authenticode. Microsoft
Defender SmartScreen może więc wyświetlić ostrzeżenie o nierozpoznanej aplikacji,
a polityka zarządzanego komputera może całkowicie zablokować jej uruchomienie.

Pobieraj betę wyłącznie z oficjalnego GitHub Actions lub GitHub Releases projektu
`Sael-Andrzej/SaelBrowser`. Przed instalacją porównaj SHA-256 pliku MSI z wartością
w dołączonym `SHA256SUMS.txt`, na przykład:

```powershell
$expected = (Get-Content .\SHA256SUMS.txt).Split()[0]
$actual = (Get-FileHash .\SaelBrowser-0.1.0-win-x64.msi -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw "Suma SHA-256 nie jest zgodna." }
```

Nie instaluj pliku, jeżeli sumy są różne. Zgodna suma potwierdza integralność
pobranego MSI względem artefaktu CI, ale nie potwierdza tożsamości wydawcy tak jak
podpis cyfrowy. Nie wyłączaj globalnie SmartScreen, programu antywirusowego ani
innych zabezpieczeń Windows.

## Zasady bezpieczeństwa

- treść stron jest niezaufanym wejściem,
- kod strony nie otrzymuje obiektów hosta .NET ani kanału web messages,
- backend evidence wymaga HTTPS i ma produkcyjny fallback,
- brak niezależnego dowodu zawsze prowadzi do `UNKNOWN`,
- clickbait nigdy nie jest automatycznie traktowany jako fałsz,
- analiza jest anulowana po każdej nawigacji.
