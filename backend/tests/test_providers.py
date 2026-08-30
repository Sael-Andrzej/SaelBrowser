import httpx
import pytest

from app.models import EvidenceRequest, Stance
from app.providers import BraveSearchProvider, GoogleFactCheckProvider, ProviderError


REQUEST = EvidenceRequest(claim="Inflacja wyniosła 3,1%.")


@pytest.mark.asyncio
async def test_google_provider_uses_official_api_and_keeps_rating_non_decisive():
    def handler(request: httpx.Request):
        assert request.url.host == "factchecktools.googleapis.com"
        return httpx.Response(200, json={"claims": [{
            "text": REQUEST.claim,
            "claimReview": [{"publisher": {"name": "Checker"},
                "url": "https://facts.example/review", "title": "Review",
                "textualRating": "False", "reviewDate": "2026-08-30"}],
        }]})
    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as client:
        result = await GoogleFactCheckProvider("secret-not-logged", client).search(REQUEST)
    assert result[0].stance == Stance.UNKNOWN
    assert result[0].provider == "google-fact-check"


@pytest.mark.asyncio
async def test_brave_result_is_discovery_not_truth_evidence():
    def handler(request: httpx.Request):
        assert request.url.host == "api.search.brave.com"
        return httpx.Response(200, json={"web": {"results": [{
            "url": "https://news.example/story", "title": "News", "description": "Snippet"
        }]}})
    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as client:
        result = await BraveSearchProvider("secret-not-logged", client).search(REQUEST)
    assert result[0].stance == Stance.UNKNOWN
    assert result[0].providerConfidence < 0.5


@pytest.mark.asyncio
async def test_malformed_provider_json_is_rejected():
    async with httpx.AsyncClient(transport=httpx.MockTransport(
        lambda _: httpx.Response(200, content=b"not-json")
    )) as client:
        with pytest.raises(ProviderError):
            await GoogleFactCheckProvider("key", client).search(REQUEST)


@pytest.mark.asyncio
async def test_provider_http_error_is_propagated_to_service_boundary():
    async with httpx.AsyncClient(transport=httpx.MockTransport(
        lambda _: httpx.Response(503)
    )) as client:
        with pytest.raises(httpx.HTTPStatusError):
            await BraveSearchProvider("key", client).search(REQUEST)


@pytest.mark.asyncio
async def test_oversized_provider_response_is_rejected():
    async with httpx.AsyncClient(transport=httpx.MockTransport(
        lambda _: httpx.Response(200, content=b"x" * (512 * 1024 + 1))
    )) as client:
        with pytest.raises(ProviderError, match="too large"):
            await BraveSearchProvider("key", client).search(REQUEST)


@pytest.mark.asyncio
async def test_provider_redirect_is_not_followed_even_to_localhost():
    def redirect(_: httpx.Request):
        return httpx.Response(302, headers={"location": "http://127.0.0.1/private"})
    async with httpx.AsyncClient(transport=httpx.MockTransport(redirect), follow_redirects=False) as client:
        with pytest.raises(httpx.HTTPStatusError):
            await GoogleFactCheckProvider("key", client).search(REQUEST)
