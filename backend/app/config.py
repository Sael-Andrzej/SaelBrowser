from __future__ import annotations

from dataclasses import dataclass
import os


def _positive_int(name: str, default: int) -> int:
    try:
        return max(1, int(os.getenv(name, str(default))))
    except ValueError:
        return default


@dataclass(frozen=True)
class Settings:
    google_factcheck_api_key: str = ""
    brave_search_api_key: str = ""
    provider_timeout_seconds: float = 4.0
    cache_ttl_seconds: int = 900
    rate_limit_requests: int = 30
    rate_limit_window_seconds: int = 60
    max_results: int = 10
    max_request_body_bytes: int = 16_384

    @classmethod
    def from_environment(cls) -> "Settings":
        return cls(
            google_factcheck_api_key=os.getenv("GOOGLE_FACTCHECK_API_KEY", ""),
            brave_search_api_key=os.getenv("BRAVE_SEARCH_API_KEY", ""),
            cache_ttl_seconds=_positive_int("SAEL_CACHE_TTL_SECONDS", 900),
            rate_limit_requests=_positive_int("SAEL_RATE_LIMIT_REQUESTS", 30),
            rate_limit_window_seconds=_positive_int("SAEL_RATE_LIMIT_WINDOW_SECONDS", 60),
        )
