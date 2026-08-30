from __future__ import annotations

from datetime import date
from enum import StrEnum
import ipaddress
from urllib.parse import urlsplit

from pydantic import BaseModel, ConfigDict, Field, field_validator


class SourceType(StrEnum):
    PRIMARY_OFFICIAL = "PRIMARY_OFFICIAL"
    PRIMARY_DOCUMENT = "PRIMARY_DOCUMENT"
    FACT_CHECK = "FACT_CHECK"
    NEWS_REPORT = "NEWS_REPORT"
    ACADEMIC = "ACADEMIC"
    SECONDARY = "SECONDARY"
    USER_GENERATED = "USER_GENERATED"
    UNKNOWN = "UNKNOWN"


class Stance(StrEnum):
    SUPPORTS = "SUPPORTS"
    REFUTES = "REFUTES"
    NEUTRAL = "NEUTRAL"
    UNKNOWN = "UNKNOWN"


class Provenance(StrEnum):
    GOOGLE_FACT_CHECK = "GOOGLE_FACT_CHECK"
    BRAVE_SEARCH = "BRAVE_SEARCH"


def validate_public_https_url(value: str, *, allow_empty: bool = False) -> str:
    if allow_empty and not value:
        return value
    if len(value) > 2_048:
        raise ValueError("URL is too long")
    parsed = urlsplit(value)
    if parsed.scheme != "https" or not parsed.hostname or parsed.username or parsed.password:
        raise ValueError("Only credential-free HTTPS URLs are allowed")
    hostname = parsed.hostname.rstrip(".").lower()
    if hostname == "localhost" or hostname.endswith(".localhost"):
        raise ValueError("Local URLs are forbidden")
    if hostname.startswith("0x") or hostname.isdigit():
        raise ValueError("Numeric host aliases are forbidden")
    ipv4_parts = hostname.split(".")
    if len(ipv4_parts) == 4 and any(len(part) > 1 and part.startswith("0") for part in ipv4_parts):
        raise ValueError("Ambiguous IPv4 notation is forbidden")
    try:
        address = ipaddress.ip_address(hostname.strip("[]"))
    except ValueError:
        address = None
    if address and not address.is_global:
        raise ValueError("Private or non-global IP addresses are forbidden")
    return value


class EvidenceRequest(BaseModel):
    model_config = ConfigDict(extra="forbid", str_strip_whitespace=True)
    claim: str = Field(min_length=8, max_length=500)
    language: str = Field(default="pl", pattern=r"^[a-z]{2}(?:-[A-Z]{2})?$")
    sourceUrl: str | None = Field(default=None, max_length=2_048)
    publishedAt: date | None = None

    @field_validator("sourceUrl")
    @classmethod
    def source_url_is_public_https(cls, value: str | None) -> str | None:
        return None if value is None else validate_public_https_url(value)


class EvidenceItem(BaseModel):
    model_config = ConfigDict(extra="forbid", str_strip_whitespace=True)
    id: str = Field(min_length=1, max_length=128)
    claim: str = Field(min_length=1, max_length=1_000)
    snippet: str = Field(min_length=1, max_length=2_000)
    url: str = Field(max_length=2_048)
    domain: str = Field(min_length=1, max_length=253)
    publisher: str = Field(min_length=1, max_length=300)
    author: str | None = Field(default=None, max_length=300)
    publishedAt: date | None = None
    eventDate: date | None = None
    sourceType: SourceType
    stance: Stance
    provenance: Provenance
    primarySourceId: str | None = Field(default=None, max_length=500)
    provider: str = Field(min_length=1, max_length=100)
    providerConfidence: float = Field(ge=0.0, le=1.0)

    @field_validator("url")
    @classmethod
    def evidence_url_is_public_https(cls, value: str) -> str:
        return validate_public_https_url(value)


class EvidenceResponse(BaseModel):
    query: str
    evidence: list[EvidenceItem]
    warnings: list[str]
    cacheHit: bool = False


class HealthResponse(BaseModel):
    status: str
    providers: dict[str, bool]
