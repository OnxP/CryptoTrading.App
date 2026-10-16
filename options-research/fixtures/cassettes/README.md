# HTTP cassettes

One JSON file per request under `<provider>/<key>.json`. The key is a hash of method, URL and sorted query params. Header values are never stored, only names (secret-bearing names are marked redacted).

`source` tells you where the body came from:

- `docs`: hand-written from the provider's public documentation before any token existed. Shapes are believed correct; values are FICTIONAL (fixture instruments, made-up prices).
- `live`: recorded by `scripts/record_cassettes.py --record` with the token present. Values are real but delayed sandbox data at `received_at`.

Tests replay cassettes with `ReplayHttpClient`; a missing cassette raises, it never falls through to the network.
