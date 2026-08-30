from __future__ import annotations

from abc import ABC, abstractmethod
from datetime import date
import hashlib
from urllib.parse import urlsplit

import httpx

from .models import EvidenceItem, EvidenceRequest, Provenance, SourceType, Stance


def _text(value: object, limit: int) -> str:
    # Internet content remains inert plain data. It is never rendered or executed.
    return " ".join(str(value or "").replace("\x00", " ").split())[:limit]


def _date(value: object) -> date | None:
    try:
        return date.fromisoformat(str(value)[:10])
    except (TypeError, ValueError):
        return None


def _id(provider: str, url: str, claim: str) -> str:
    return hashlib.sha256(f"{provider}\x1f{url}\x1f{claim}".encode()).hexdigest()[:24]


class ProviderError(RuntimeError):
    pass


async def _get_json_limited(client: httpx.AsyncClient, url: str, **kwargs) -> dict:
    async with client.stream("GET", url, **kwargs) as response:
        response.raise_for_status()
        declared = response.headers.get("content-length")
        if declared and (not declared.isdigit() or int(declared) > MAX_PROVIDER_RESPONSE_BYTES):
            raise ProviderError("provider response too large")
        chunks = bytearray()
        async for chunk in response.aiter_bytes():
            chunks.extend(chunk)
            if len(chunks) > MAX_PROVIDER_RESPONSE_BYTES:
                raise ProviderError("provider response too large")
    try:
        parsed = httpx.Response(200, content=bytes(chunks)).json()
    except ValueError as error:
        raise ProviderError("malformed provider response") from error
    if not isinstance(parsed, dict):
        raise ProviderError("malformed provider response")
    return parsed


class EvidenceProvider(ABC):
    name: str
    available: bool

    @abstractmethod
    async def search(self, request: EvidenceRequest) -> list[EvidenceItem]: ...


class GoogleFactCheckProvider(EvidenceProvider):
    name = "google-fact-check"

    def __init__(self, api_key: str, client: httpx.AsyncClient) -> None:
        self._api_key = api_key
        self._client = client
        self.available = bool(api_key)

    async def search(self, request: EvidenceRequest) -> list[EvidenceItem]:
        if not self.available:
            return []
        payload = await _get_json_limited(self._client,
            "https://factchecktools.googleapis.com/v1alpha1/claims:search",
            params={"query": request.claim, "languageCode": request.language, "key": self._api_key},
        )
        try:
            claims = payload.get("claims", [])
            if not isinstance(claims, list):
                raise TypeError
        except (AttributeError, TypeError) as error:
            raise ProviderError("malformed provider response") from error
        output: list[EvidenceItem] = []
        for claim in claims[:5]:
            claim_text = _text(claim.get("text"), 1_000) or request.claim
            for review in claim.get("claimReview", [])[:2]:
                url = _text(review.get("url"), 2_048)
                publisher = _text((review.get("publisher") or {}).get("name"), 300) or "Fact checker"
                rating = _text(review.get("textualRating"), 300)
                title = _text(review.get("title"), 1_000)
                snippet = " — ".join(part for part in (title, rating) if part)
                domain = (urlsplit(url).hostname or "").lower()
                if not snippet or not domain:
                    continue
                try:
                    output.append(EvidenceItem(
                        id=_id(self.name, url, claim_text), claim=claim_text, snippet=snippet,
                        url=url, domain=domain, publisher=publisher,
                        publishedAt=_date(review.get("reviewDate")), eventDate=_date(claim.get("claimDate")),
                        sourceType=SourceType.FACT_CHECK, stance=Stance.UNKNOWN,
                        provenance=Provenance.GOOGLE_FACT_CHECK, primarySourceId=url,
                        provider=self.name, providerConfidence=0.75,
                    ))
                except ValueError:
                    continue
        return output


class BraveSearchProvider(EvidenceProvider):
    name = "brave-search"

    def __init__(self, api_key: str, client: httpx.AsyncClient) -> None:
        self._api_key = api_key
        self._client = client
        self.available = bool(api_key)

    async def search(self, request: EvidenceRequest) -> list[EvidenceItem]:
        if not self.available:
            return []
        payload = await _get_json_limited(self._client,
            "https://api.search.brave.com/res/v1/web/search",
            headers={"Accept": "application/json", "X-Subscription-Token": self._api_key},
            params={"q": request.claim, "count": 10, "search_lang": request.language.split("-")[0]},
        )
        try:
            results = payload.get("web", {}).get("results", [])
            if not isinstance(results, list):
                raise TypeError
        except (AttributeError, TypeError) as error:
            raise ProviderError("malformed provider response") from error
        output: list[EvidenceItem] = []
        for item in results[:10]:
            url = _text(item.get("url"), 2_048)
            domain = (urlsplit(url).hostname or "").lower()
            title = _text(item.get("title"), 1_000)
            snippet = _text(item.get("description"), 2_000)
            if not domain or not snippet:
                continue
            try:
                output.append(EvidenceItem(
                    id=_id(self.name, url, request.claim), claim=request.claim,
                    snippet=" — ".join(part for part in (title, snippet) if part),
                    url=url, domain=domain, publisher=domain,
                    publishedAt=_date(item.get("page_age")), sourceType=SourceType.UNKNOWN,
                    stance=Stance.UNKNOWN, provenance=Provenance.BRAVE_SEARCH,
                    primarySourceId=None, provider=self.name, providerConfidence=0.35,
                ))
            except ValueError:
                continue
        return output


MAX_PROVIDER_RESPONSE_BYTES = 512 * 1_024
