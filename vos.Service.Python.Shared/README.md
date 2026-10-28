# vos.Service.Python.Shared — the managed-microservice contract, for Python

The plumbing every Python VillageOS service needs, so a service writes its handler and nothing else. It is the
Python counterpart of the C# [`vos.Auth.Shared`](../vos.Auth.Shared) and
[`vos.Service.Shared`](../vos.Service.Shared) libraries. The contract it implements is in
[docs/SERVICE_AUTHORING.md](../docs/SERVICE_AUTHORING.md) and [docs/SERVICE_CONTRACT.md](../docs/SERVICE_CONTRACT.md).

The importable package is `vos_service`. Nothing domain-specific belongs here: a helper moves in when a second
service needs it.

## Use it from a service

A service installs it from the folder, as a path dependency, from its own `requirements.txt`:

```
-e ../vos.Service.Python.Shared
```

Nothing is published to a package index.

## Test

```bash
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
.venv/bin/python -m pytest
```
