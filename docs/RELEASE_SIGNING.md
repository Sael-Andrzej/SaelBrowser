# Podpisywanie wydania Android

Build release wymaga czterech zmiennych środowiskowych:

- `SAEL_RELEASE_STORE_FILE` — bezwzględna ścieżka do keystore,
- `SAEL_RELEASE_STORE_PASSWORD` — hasło keystore,
- `SAEL_RELEASE_KEY_ALIAS` — alias klucza,
- `SAEL_RELEASE_KEY_PASSWORD` — hasło klucza.

Keystore i hasła muszą być przechowywane poza repozytorium. Gradle celowo
przerywa `assembleRelease` i `bundleRelease`, jeżeli konfiguracja nie jest
kompletna, aby nie powstał przypadkowy niepodpisany artefakt.

Po ustawieniu zmiennych końcowy build wykonuje:

```powershell
.\gradlew.bat clean test lint assembleRelease bundleRelease
```

Artefakty znajdują się w:

- `app/build/outputs/apk/release/`,
- `app/build/outputs/bundle/release/`.

Keystore należy przechowywać w co najmniej dwóch bezpiecznych, zaszyfrowanych
kopiach. Utrata klucza może uniemożliwić publikowanie aktualizacji aplikacji.
