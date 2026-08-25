# Sael Browser Android MVP

Pierwsza natywna wersja Android przeglądarki bez reklam.

## Już działa
- WebView jako przeglądarka
- adres / wyszukiwanie
- wstecz / dalej
- blokowanie części reklam i trackerów po URL
- CZYSTY / ORYGINAŁ
- czyszczenie typowych reklam, popupów, newsletterów i banerów w DOM
- żółty status: treść nieweryfikowana

## Jeszcze nie udajemy, że działa
- prawdziwy FactEngine 🟢🟡🔴
- przepisywanie tytułu na podstawie faktów
- ekstrakcja artykułu i pełne przeredagowanie treści
- pełne listy filtrów reklamowych

## Budowa bez komputera przez GitHub
1. Utwórz nowe repozytorium GitHub.
2. Wgraj całą zawartość ZIP-a do repozytorium.
3. Wejdź w Actions -> Build Android APK -> Run workflow.
4. Po zakończeniu pobierz artifact `SaelBrowser-debug-apk`.
5. Rozpakuj artifact na telefonie i zainstaluj `app-debug.apk`.

Android może poprosić o zgodę na instalowanie aplikacji z przeglądarki/menedżera plików.
