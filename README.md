# SaelBrowser

Natywna przeglądarka Android z trybem czytelnym SAEL i konserwatywną analizą
dowodów dla twierdzeń zawartych w artykułach.

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
