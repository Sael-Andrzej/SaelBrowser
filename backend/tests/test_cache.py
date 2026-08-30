from app.cache import TtlCache, cache_key
from app.models import EvidenceRequest, EvidenceResponse


def test_cache_expiry():
    now = [10.0]
    cache = TtlCache(5, clock=lambda: now[0])
    key = "key"
    cache.put(key, EvidenceResponse(query="claim", evidence=[], warnings=[]))
    assert cache.get(key) is not None
    now[0] = 15.0
    assert cache.get(key) is None


def test_cache_key_normalizes_claim_and_includes_context():
    first = EvidenceRequest(claim="  Inflacja   WYNOSI 3,1%. ", publishedAt="2026-08-30", language="pl")
    equivalent = EvidenceRequest(claim="inflacja wynosi 3,1%.", publishedAt="2026-08-30", language="pl")
    other_date = EvidenceRequest(claim="inflacja wynosi 3,1%.", publishedAt="2025-08-30", language="pl")
    assert cache_key(first) == cache_key(equivalent)
    assert cache_key(first) != cache_key(other_date)


def test_cache_has_hard_entry_limit():
    cache = TtlCache(60, max_entries=2)
    response = EvidenceResponse(query="claim", evidence=[], warnings=[])
    cache.put("one", response)
    cache.put("two", response)
    cache.put("three", response)
    assert cache.get("one") is None
    assert cache.get("two") is not None
    assert cache.get("three") is not None
