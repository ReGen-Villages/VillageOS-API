from __future__ import annotations

import os
import sys
import uuid
from collections.abc import Awaitable, Callable
from contextlib import asynccontextmanager

from fastapi import Depends, FastAPI, Request
from fastapi.responses import JSONResponse

from .auth import verifier
from .config import Config, usage
from .mycelium import MyceliumClient

Handler = Callable[[dict, int], Awaitable[dict]]


class ServiceHost:
    """A managed microservice: the four contract endpoints around a handler the service supplies.

    `handle(payload, request_number)` answers a relationship Mycelium calls it for and returns the
    JSON body to send back. A service adds its own routes to `app`, protecting them with
    `verify_request`.
    """

    def __init__(self, service_name: str, config: Config, handle: Handler, *, version: str = "1.0.0", title: str | None = None):
        self.service_name = service_name
        self.config = config
        self.version = version
        self.handler_id = str(uuid.uuid4())
        self.requests_processed = 0
        self.mycelium = MyceliumClient(config)
        self.verify_request = verifier(config)
        self._handle = handle
        #: The uvicorn server, once `run` has started it; `/shutdown` asks it to stop.
        self.server = None
        self.app = FastAPI(title=title or f"VillageOS {service_name} Microservice", lifespan=self._lifespan)
        self._add_routes()

    @asynccontextmanager
    async def _lifespan(self, _: FastAPI):
        if not (self.config.api_key or self.config.token):
            print("neither ApiKey nor Token is set, so this service has not registered with mycelium")
        else:
            try:
                registered = await self.mycelium.register(self.service_name, self.handler_id)
                print(f"{'registered' if registered else 'failed to register'} with mycelium as {self.handler_id}")
            except Exception as exc:
                print(f"registration failed: {exc}", file=sys.stderr)
        yield

    def _add_routes(self) -> None:
        @self.app.get("/health")
        def health() -> dict:
            return {
                "status": "Healthy",
                "service": self.service_name,
                "requestsProcessed": self.requests_processed,
                "processId": os.getpid(),
            }

        @self.app.get("/stats")
        def stats() -> dict:
            return {
                "service": self.service_name,
                "version": self.version,
                "requestsProcessed": self.requests_processed,
                "handlerId": self.handler_id,
                "myceliumUrl": self.config.mycelium_url,
            }

        @self.app.post("/handle")
        async def handle(request: Request, _: None = Depends(self.verify_request)) -> JSONResponse:
            self.requests_processed += 1
            return JSONResponse(await self._handle(await request.json(), self.requests_processed))

        @self.app.post("/shutdown")
        def shutdown(_: None = Depends(self.verify_request)) -> dict:
            # Mycelium kills a daemon it started itself. One started any other way is stopped through this
            # route alone, and Mycelium then checks that it no longer answers.
            if self.server is not None:
                self.server.should_exit = True
            return {"message": f"Shutting down {self.service_name} microservice"}

    def run(self, program: str) -> None:
        """Serve on the configured port; with none, print how to start `program` and exit."""
        if self.config.port == 0:
            print(usage(program), file=sys.stderr)
            sys.exit(1)
        import uvicorn

        print(
            f"VillageOS {self.service_name} microservice — port {self.config.port}, "
            f"mycelium {self.config.mycelium_url}, auth={bool(self.config.verification_key)}"
        )
        self.server = uvicorn.Server(uvicorn.Config(self.app, host="localhost", port=self.config.port, log_level="info"))
        self.server.run()
