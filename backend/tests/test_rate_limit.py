from app.rate_limit import RateLimiter


def test_rate_limiter_state_is_bounded():
    limiter = RateLimiter(limit=1, window_seconds=60, max_clients=2)
    assert limiter.allow("one")
    assert limiter.allow("two")
    assert limiter.allow("three")
    assert len(limiter._requests) == 2
