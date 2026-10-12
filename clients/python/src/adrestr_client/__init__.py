"""Python client for the AdresTR REST API (Turkish address parsing, matching and validation)."""

from __future__ import annotations

from typing import Any, Iterable, Optional

import httpx

__all__ = ["AdresTRClient", "AsyncAdresTRClient", "AdresTRError"]
__version__ = "0.1.0"

DEFAULT_BASE_URL = "http://localhost:8080"
_USER_AGENT = f"adrestr-client-python/{__version__}"


class AdresTRError(Exception):
    """Raised for non-2xx responses. Carries the RFC 7807 problem details when the API sent them."""

    def __init__(self, status: int, title: str, detail: Optional[str] = None, errors: Optional[dict] = None):
        super().__init__(f"{status} {title}" + (f": {detail}" if detail else ""))
        self.status = status
        self.title = title
        self.detail = detail
        self.errors = errors or {}


def _raise_for_status(response: httpx.Response) -> None:
    if response.is_success:
        return
    try:
        problem = response.json()
        raise AdresTRError(
            response.status_code,
            problem.get("title") or response.reason_phrase,
            problem.get("detail"),
            problem.get("errors"),
        )
    except ValueError:
        raise AdresTRError(response.status_code, response.reason_phrase, response.text[:200]) from None


def _drop_none(params: dict) -> dict:
    return {k: v for k, v in params.items() if v is not None}


class _Spec:
    """Request descriptions shared by the sync and async clients."""

    @staticmethod
    def parse(text: str, trace: bool) -> dict:
        return {"method": "POST", "url": "/v1/parse", "params": {"trace": "true"} if trace else None, "json": {"text": text}}

    @staticmethod
    def parse_batch(items: Iterable[str]) -> dict:
        return {"method": "POST", "url": "/v1/parse/batch", "json": {"items": list(items)}}

    @staticmethod
    def parse_csv(csv_text: str, column: Optional[str]) -> dict:
        return {
            "method": "POST",
            "url": "/v1/parse/csv",
            "params": _drop_none({"column": column}),
            "content": csv_text.encode("utf-8"),
            "headers": {"Content-Type": "text/csv; charset=utf-8"},
        }

    @staticmethod
    def validate(il: Optional[str], ilce: Optional[str], mahalle: Optional[str], posta_kodu: Optional[str]) -> dict:
        return {"method": "POST", "url": "/v1/validate", "json": {"il": il, "ilce": ilce, "mahalle": mahalle, "postaKodu": posta_kodu}}

    @staticmethod
    def match(a: str, b: str) -> dict:
        return {"method": "POST", "url": "/v1/match", "json": {"a": a, "b": b}}

    @staticmethod
    def dedupe(items: Iterable[str], minimum: Optional[str]) -> dict:
        return {"method": "POST", "url": "/v1/dedupe", "json": _drop_none({"items": list(items), "minimum": minimum})}

    @staticmethod
    def get(url: str, params: Optional[dict] = None) -> dict:
        return {"method": "GET", "url": url, "params": _drop_none(params or {})}


class AdresTRClient:
    """Synchronous client.

    >>> client = AdresTRClient("https://my-adrestr.example.com")
    >>> client.parse("kadikoy caferaga mh moda cd no:12")["resolved"]["birim"]["name"]
    'Caferağa'
    """

    def __init__(
        self,
        base_url: str = DEFAULT_BASE_URL,
        *,
        timeout: float = 30.0,
        transport: Optional[httpx.BaseTransport] = None,
        headers: Optional[dict] = None,
    ):
        self._http = httpx.Client(
            base_url=base_url, timeout=timeout, transport=transport, headers={"User-Agent": _USER_AGENT, **(headers or {})}
        )

    def close(self) -> None:
        self._http.close()

    def __enter__(self) -> "AdresTRClient":
        return self

    def __exit__(self, *exc: Any) -> None:
        self.close()

    def _send(self, spec: dict) -> httpx.Response:
        response = self._http.request(**spec)
        _raise_for_status(response)
        return response

    def parse(self, text: str, *, trace: bool = False) -> dict:
        """Parse one address. ``trace=True`` adds a ``trace`` object explaining the result."""
        return self._send(_Spec.parse(text, trace)).json()

    def parse_batch(self, items: Iterable[str]) -> list[dict]:
        """Parse up to 1,000 addresses; results are in request order."""
        return self._send(_Spec.parse_batch(items)).json()["results"]

    def parse_csv(self, csv_text: str, column: Optional[str] = None) -> str:
        """Clean the address column (default ``adres``) of a CSV and return the CSV with appended columns."""
        return self._send(_Spec.parse_csv(csv_text, column)).text

    def validate(
        self, *, il: Optional[str] = None, ilce: Optional[str] = None, mahalle: Optional[str] = None, posta_kodu: Optional[str] = None
    ) -> dict:
        """Check that a structured il / ilçe / mahalle / postal code combination exists and is consistent."""
        return self._send(_Spec.validate(il, ilce, mahalle, posta_kodu)).json()

    def match(self, a: str, b: str) -> dict:
        """Decide whether two addresses are the same place (``level``: Different … SameFlat)."""
        return self._send(_Spec.match(a, b)).json()

    def dedupe(self, items: Iterable[str], *, minimum: Optional[str] = None) -> list[list[int]]:
        """Group duplicate addresses; returns groups of indexes into ``items``."""
        return self._send(_Spec.dedupe(items, minimum)).json()["groups"]

    def provinces(self) -> list[dict]:
        return self._send(_Spec.get("/v1/provinces")).json()

    def districts(self, plaka: int) -> list[dict]:
        return self._send(_Spec.get(f"/v1/provinces/{plaka}/districts")).json()

    def units(self, district_id: int) -> list[dict]:
        return self._send(_Spec.get(f"/v1/districts/{district_id}/units")).json()

    def unit(self, unit_id: int) -> dict:
        return self._send(_Spec.get(f"/v1/units/{unit_id}")).json()

    def autocomplete(
        self, q: str, *, level: Optional[str] = None, plaka: Optional[int] = None, district_id: Optional[int] = None
    ) -> list[dict]:
        """Prefix suggestions; ``level`` is ``province``, ``district`` or ``unit`` (default)."""
        return self._send(_Spec.get("/v1/autocomplete", {"q": q, "level": level, "plaka": plaka, "districtId": district_id})).json()


class AsyncAdresTRClient:
    """Asynchronous client with the same methods as :class:`AdresTRClient` (all ``async``)."""

    def __init__(
        self,
        base_url: str = DEFAULT_BASE_URL,
        *,
        timeout: float = 30.0,
        transport: Optional[httpx.AsyncBaseTransport] = None,
        headers: Optional[dict] = None,
    ):
        self._http = httpx.AsyncClient(
            base_url=base_url, timeout=timeout, transport=transport, headers={"User-Agent": _USER_AGENT, **(headers or {})}
        )

    async def aclose(self) -> None:
        await self._http.aclose()

    async def __aenter__(self) -> "AsyncAdresTRClient":
        return self

    async def __aexit__(self, *exc: Any) -> None:
        await self.aclose()

    async def _send(self, spec: dict) -> httpx.Response:
        response = await self._http.request(**spec)
        _raise_for_status(response)
        return response

    async def parse(self, text: str, *, trace: bool = False) -> dict:
        return (await self._send(_Spec.parse(text, trace))).json()

    async def parse_batch(self, items: Iterable[str]) -> list[dict]:
        return (await self._send(_Spec.parse_batch(items))).json()["results"]

    async def parse_csv(self, csv_text: str, column: Optional[str] = None) -> str:
        return (await self._send(_Spec.parse_csv(csv_text, column))).text

    async def validate(
        self, *, il: Optional[str] = None, ilce: Optional[str] = None, mahalle: Optional[str] = None, posta_kodu: Optional[str] = None
    ) -> dict:
        return (await self._send(_Spec.validate(il, ilce, mahalle, posta_kodu))).json()

    async def match(self, a: str, b: str) -> dict:
        return (await self._send(_Spec.match(a, b))).json()

    async def dedupe(self, items: Iterable[str], *, minimum: Optional[str] = None) -> list[list[int]]:
        return (await self._send(_Spec.dedupe(items, minimum))).json()["groups"]

    async def provinces(self) -> list[dict]:
        return (await self._send(_Spec.get("/v1/provinces"))).json()

    async def districts(self, plaka: int) -> list[dict]:
        return (await self._send(_Spec.get(f"/v1/provinces/{plaka}/districts"))).json()

    async def units(self, district_id: int) -> list[dict]:
        return (await self._send(_Spec.get(f"/v1/districts/{district_id}/units"))).json()

    async def unit(self, unit_id: int) -> dict:
        return (await self._send(_Spec.get(f"/v1/units/{unit_id}"))).json()

    async def autocomplete(
        self, q: str, *, level: Optional[str] = None, plaka: Optional[int] = None, district_id: Optional[int] = None
    ) -> list[dict]:
        spec = _Spec.get("/v1/autocomplete", {"q": q, "level": level, "plaka": plaka, "districtId": district_id})
        return (await self._send(spec)).json()
