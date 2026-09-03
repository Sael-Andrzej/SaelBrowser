# SaelBrowser

Przeglądarka dla Androida i Windows z trybem czytelnym SAEL oraz konserwatywną
analizą dowodów dla twierdzeń zawartych w artykułach.

## Już działa
- WebView jako przeglądarka
- adres / wyszukiwanie
- wstecz / dalej
- blokowanie części reklam i trackerów po URL
- SAEL / ORYGINAŁ
- czyszczenie typowych reklam, popupów, newsletterów i banerów w DOM
- ekstrakcja treści i twierdzeń bez modyfikowania oryginalnej strony
- analiza clickbaitu niezależna od werdyktu
- FactEngine z wynikami PRAWDA / FAŁSZ / NIE WIEM i bezpiecznym progiem pewności
- produkcyjny Evidence Backend z Google Fact Check i zapasową domeną API
- podpisane wydania APK/AAB konfigurowane bez sekretów w repozytorium

Brak wystarczających niezależnych dowodów zawsze prowadzi do wyniku NIE WIEM.
Clickbait nie jest automatycznie utożsamiany z fałszem.

## Budowa

```powershell
.\gradlew.bat clean test lint assembleDebug
```

Konfiguracja podpisanego release jest opisana w
[`docs/RELEASE_SIGNING.md`](docs/RELEASE_SIGNING.md).

Android może poprosić o zgodę na instalowanie aplikacji z przeglądarki/menedżera plików.

## Windows 10/11

Wersja Windows używa natywnego WPF oraz Microsoft Edge WebView2. Kod znajduje się
w katalogu [`windows`](windows), razem z testami i projektem instalatora MSI.

> **Windows jest obecnie udostępniany jako niepodpisana wersja beta.** Instalator
> MSI i aplikacja nie mają podpisu Authenticode, dlatego Microsoft Defender
> SmartScreen może wyświetlić ostrzeżenie o nierozpoznanej aplikacji. Pobieraj betę
> wyłącznie z oficjalnych artefaktów tego repozytorium i przed instalacją porównaj
> SHA-256 pliku MSI z dołączonym `SHA256SUMS.txt`. Zgodna suma potwierdza integralność
> pobranego pliku, ale nie zastępuje podpisu zweryfikowanego wydawcy. Nie wyłączaj
> globalnie SmartScreen ani innych zabezpieczeń Windows.

```powershell
dotnet test windows/tests/SaelBrowser.Core.Tests/SaelBrowser.Core.Tests.csproj -c Release
dotnet publish windows/src/SaelBrowser.Windows/SaelBrowser.Windows.csproj -c Release -r win-x64 --self-contained true -o windows/artifacts/publish/win-x64
dotnet build windows/installer/SaelBrowser.Installer/SaelBrowser.Installer.wixproj -c Release
```
