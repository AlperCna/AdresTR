import json

import httpx
import pytest

from adrestr_client import AdresTRClient, AdresTRError, AsyncAdresTRClient


def handler(request: httpx.Request) -> httpx.Response:
    path, query = request.url.path, dict(request.url.params)
    body = json.loads(request.content) if request.content and request.headers.get("content-type", "").startswith("application/json") else None
    if path == "/v1/parse":
        if body["text"] == "bad":
            return httpx.Response(400, json={"title": "One or more validation errors occurred.", "errors": {"text": ["too long"]}})
        return httpx.Response(200, json={"input": body["text"], "trace": {"tokens": []} if query.get("trace") == "true" else None})
    if path == "/v1/parse/batch":
        return httpx.Response(200, json={"results": [{"input": i} for i in body["items"]]})
    if path == "/v1/parse/csv":
        assert request.headers["content-type"].startswith("text/csv")
        return httpx.Response(200, text=request.content.decode() + "|" + query.get("column", "adres"))
    if path == "/v1/validate":
        return httpx.Response(200, json={"valid": True, "echo": body})
    if path == "/v1/match":
        return httpx.Response(200, json={"level": "SameFlat", "a": body["a"], "b": body["b"]})
    if path == "/v1/dedupe":
        return httpx.Response(200, json={"groups": [[0, 2], [1]], "minimum": body.get("minimum")})
    if path == "/v1/provinces":
        return httpx.Response(200, json=[{"plaka": 34, "name": "İstanbul"}])
    if path == "/v1/provinces/34/districts":
        return httpx.Response(200, json=[{"id": 3423, "name": "Kadıköy"}])
    if path == "/v1/districts/3423/units":
        return httpx.Response(200, json=[{"id": 34230005, "name": "Caferağa"}])
    if path == "/v1/units/34230005":
        return httpx.Response(200, json={"id": 34230005, "name": "Caferağa"})
    if path == "/v1/autocomplete":
        return httpx.Response(200, json=[{"q": query["q"], "level": query.get("level"), "districtId": query.get("districtId")}])
    return httpx.Response(404, json={"title": "Not Found"})


@pytest.fixture
def client():
    with AdresTRClient("http://test", transport=httpx.MockTransport(handler)) as c:
        yield c


def test_parse_and_trace(client):
    assert client.parse("Moda")["input"] == "Moda"
    assert client.parse("Moda", trace=True)["trace"] == {"tokens": []}
    assert client.parse("Moda")["trace"] is None


def test_batch_csv_validate_match_dedupe(client):
    assert [r["input"] for r in client.parse_batch(["a", "b"])] == ["a", "b"]
    assert client.parse_csv("id,adres\n1,x\n", column="adres") == "id,adres\n1,x\n|adres"
    assert client.validate(il="İzmir", posta_kodu="35220")["echo"] == {"il": "İzmir", "ilce": None, "mahalle": None, "postaKodu": "35220"}
    assert client.match("a", "b")["level"] == "SameFlat"
    assert client.dedupe(["a", "b", "c"], minimum="SameStreet") == [[0, 2], [1]]


def test_reference_and_autocomplete(client):
    assert client.provinces()[0]["plaka"] == 34
    assert client.districts(34)[0]["id"] == 3423
    assert client.units(3423)[0]["name"] == "Caferağa"
    assert client.unit(34230005)["id"] == 34230005
    assert client.autocomplete("cafer", district_id=3423) == [{"q": "cafer", "level": None, "districtId": "3423"}]


def test_errors_carry_problem_details(client):
    with pytest.raises(AdresTRError) as error:
        client.parse("bad")
    assert error.value.status == 400
    assert error.value.errors == {"text": ["too long"]}


def test_non_json_errors_are_wrapped():
    def broken(_: httpx.Request) -> httpx.Response:
        return httpx.Response(502, text="Bad gateway")

    with AdresTRClient("http://test", transport=httpx.MockTransport(broken)) as c, pytest.raises(AdresTRError) as error:
        c.parse("x")
    assert error.value.status == 502


@pytest.mark.asyncio
async def test_async_client():
    async with AsyncAdresTRClient("http://test", transport=httpx.MockTransport(handler)) as c:
        assert (await c.parse("Moda", trace=True))["trace"] == {"tokens": []}
        assert [r["input"] for r in await c.parse_batch(["a"])] == ["a"]
        assert (await c.match("a", "b"))["level"] == "SameFlat"
        assert await c.dedupe(["a", "b", "c"]) == [[0, 2], [1]]
        assert (await c.provinces())[0]["name"] == "İstanbul"
        with pytest.raises(AdresTRError):
            await c.parse("bad")
