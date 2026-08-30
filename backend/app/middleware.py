from __future__ import annotations

import json
from starlette.types import ASGIApp, Message, Receive, Scope, Send


class BodySizeLimitMiddleware:
    """Buffers only the small allowed request body and rejects chunked oversize input."""

    def __init__(self, app: ASGIApp, max_bytes: int) -> None:
        self.app = app
        self.max_bytes = max_bytes

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return
        content_length = dict(scope.get("headers", [])).get(b"content-length")
        if content_length:
            try:
                if int(content_length) > self.max_bytes:
                    await self._reject(send)
                    return
            except ValueError:
                await self._reject(send)
                return
        messages: list[Message] = []
        size = 0
        while True:
            message = await receive()
            messages.append(message)
            if message["type"] == "http.disconnect":
                return
            if message["type"] == "http.request":
                size += len(message.get("body", b""))
                if size > self.max_bytes:
                    await self._reject(send)
                    return
                if not message.get("more_body", False):
                    break
        content_type = dict(scope.get("headers", [])).get(b"content-type", b"").lower()
        if b"json" in content_type:
            body = b"".join(message.get("body", b"") for message in messages
                            if message["type"] == "http.request")
            if self._json_too_deep(body):
                await self._reject(send, status=400, detail="JSON nesting limit exceeded")
                return
        index = 0

        async def replay() -> Message:
            nonlocal index
            if index < len(messages):
                result = messages[index]
                index += 1
                return result
            return {"type": "http.request", "body": b"", "more_body": False}

        await self.app(scope, replay, send)

    @staticmethod
    async def _reject(send: Send, status: int = 413,
                      detail: str = "Request body too large") -> None:
        body = json.dumps({"detail": detail}).encode()
        await send({"type": "http.response.start", "status": status,
                    "headers": [(b"content-type", b"application/json"),
                                (b"content-length", str(len(body)).encode())]})
        await send({"type": "http.response.body", "body": body})

    @staticmethod
    def _json_too_deep(body: bytes, max_depth: int = 32) -> bool:
        depth = 0
        in_string = False
        escaped = False
        for byte in body:
            if in_string:
                if escaped:
                    escaped = False
                elif byte == 0x5C:
                    escaped = True
                elif byte == 0x22:
                    in_string = False
                continue
            if byte == 0x22:
                in_string = True
            elif byte in (0x5B, 0x7B):
                depth += 1
                if depth > max_depth:
                    return True
            elif byte in (0x5D, 0x7D):
                depth = max(0, depth - 1)
        return False
