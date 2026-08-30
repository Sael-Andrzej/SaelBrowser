# SAEL Evidence Backend

Mały backend FastAPI, który wyszukuje i normalizuje potencjalne dowody. Nie wydaje werdyktu.

## Lokalnie

```bash
python -m venv .venv
.venv/bin/pip install -e '.[test]'
.venv/bin/pytest
.venv/bin/uvicorn app.main:app --host 127.0.0.1 --port 8080
```

`POST /api/v1/evidence` przyjmuje tylko claim, język, publiczny HTTPS `sourceUrl` i datę publikacji. `sourceUrl` nie jest pobierany. Klucze `GOOGLE_FACTCHECK_API_KEY` i `BRAVE_SEARCH_API_KEY` są opcjonalnymi zmiennymi środowiskowymi. Bez nich endpoint bezpiecznie zwraca pustą listę i ostrzeżenia.

## Kontener

```bash
cp .env.example .env
# Ustaw SAEL_BACKEND_PORT po sprawdzeniu zajętych portów, np. 18080.
docker compose up --build
curl http://127.0.0.1:18080/health
```

Kontener działa jako UID 10001, bez capabilities i `privileged`, z read-only filesystem, limitami CPU/RAM/PID i portem związanym wyłącznie z localhost. Publiczny HTTPS powinien kończyć się w istniejącym Nginx. Sekretów nie należy przekazywać przez argumenty obrazu ani umieszczać w repozytorium.
