from __future__ import annotations

from contextlib import asynccontextmanager
import logging

import httpx
from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from .cache import TtlCache
from .config import Settings
from .models import EvidenceRequest, EvidenceResponse, HealthResponse
from .middleware import BodySizeLimitMiddleware
from .providers import BraveSearchProvider, GoogleFactCheckProvider
from .rate_limit import RateLimiter
from .service import EvidenceService

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")


def create_app(settings: Settings | None = None, service: EvidenceService | None = None) -> FastAPI:
    config = settings or Settings.from_environment()
    client = httpx.AsyncClient(timeout=httpx.Timeout(config.provider_timeout_seconds), follow_redirects=False)
    providers = [
        GoogleFactCheckProvider(config.google_factcheck_api_key, client),
        BraveSearchProvider(config.brave_search_api_key, client),
    ]
    evidence_service = service or EvidenceService(
        providers, TtlCache(config.cache_ttl_seconds), config.provider_timeout_seconds, config.max_results
    )
    limiter = RateLimiter(config.rate_limit_requests, config.rate_limit_window_seconds)

    @asynccontextmanager
    async def lifespan(_: FastAPI):
        yield
        await client.aclose()

    app = FastAPI(title="SAEL Evidence Backend", version="0.1.0", lifespan=lifespan)
    app.add_middleware(BodySizeLimitMiddleware, max_bytes=config.max_request_body_bytes)
    app.state.evidence_service = evidence_service

    @app.middleware("http")
    async def request_guards(request: Request, call_next):
        client_host = request.client.host if request.client else "unknown"
        if request.url.path.startswith("/api/") and not limiter.allow(client_host):
            return JSONResponse({"detail": "Rate limit exceeded"}, status_code=429)
        return await call_next(request)

    @app.get("/health", response_model=HealthResponse)
    async def health() -> HealthResponse:
        return HealthResponse(status="ok", providers={item.name: item.available for item in providers})

    @app.post("/api/v1/evidence", response_model=EvidenceResponse)
    async def evidence(body: EvidenceRequest, request: Request) -> EvidenceResponse:
        return await request.app.state.evidence_service.search(body)

    return app


app = create_app()
