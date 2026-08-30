import pytest

from app.cache import TtlCache
from app.models import EvidenceRequest, Stance
from app.service import EvidenceService
from tests.helpers import FakeProvider, item


REQUEST = EvidenceRequest(claim="Inflacja wyniosła 3,1%.")


@pytest.mark.asyncio
async def test_provider_timeout_is_warning_not_failure():
    service = EvidenceService([FakeProvider(delay=0.1)], TtlCache(60), timeout=0.01, max_results=10)
    response = await service.search(REQUEST)
    assert response.evidence == []
    assert response.warnings == ["fake: timeout"]


@pytest.mark.asyncio
async def test_provider_http_error_is_warning_not_failure():
    service = EvidenceService([FakeProvider(error=RuntimeError("HTTP 500"))], TtlCache(60), 1, 10)
    response = await service.search(REQUEST)
    assert response.evidence == []
    assert response.warnings == ["fake: error"]


@pytest.mark.asyncio
async def test_zero_results_is_safe():
    response = await EvidenceService([FakeProvider()], TtlCache(60), 1, 10).search(REQUEST)
    assert response.evidence == []


@pytest.mark.asyncio
async def test_cache_hit_avoids_provider_quota():
    provider = FakeProvider([item()])
    service = EvidenceService([provider], TtlCache(60), 1, 10)
    first = await service.search(REQUEST)
    second = await service.search(REQUEST)
    assert not first.cacheHit
    assert second.cacheHit
    assert provider.calls == 1


@pytest.mark.asyncio
async def test_concurrent_identical_requests_share_one_provider_call():
    provider = FakeProvider([item()], delay=0.02)
    service = EvidenceService([provider], TtlCache(60), 1, 10)
    first, second = await __import__("asyncio").gather(service.search(REQUEST), service.search(REQUEST))
    assert provider.calls == 1
    assert sorted((first.cacheHit, second.cacheHit)) == [False, True]


@pytest.mark.asyncio
async def test_duplicates_are_preserved_for_android_independence_analysis():
    duplicate = item()
    response = await EvidenceService([FakeProvider([duplicate, duplicate])], TtlCache(60), 1, 10).search(REQUEST)
    assert len(response.evidence) == 2
    assert response.evidence[0].primarySourceId == response.evidence[1].primarySourceId


@pytest.mark.asyncio
async def test_conflicting_items_are_data_not_backend_verdict():
    results = [item("support", stance=Stance.SUPPORTS), item("refute", stance=Stance.REFUTES)]
    response = await EvidenceService([FakeProvider(results)], TtlCache(60), 1, 10).search(REQUEST)
    assert {entry.stance for entry in response.evidence} == {Stance.SUPPORTS, Stance.REFUTES}
    assert not hasattr(response, "verdict")


@pytest.mark.asyncio
async def test_result_limit_caps_large_provider_response():
    results = [item(str(index), domain=f"source{index}.example") for index in range(100)]
    response = await EvidenceService([FakeProvider(results)], TtlCache(60), 1, 5).search(REQUEST)
    assert len(response.evidence) == 5
