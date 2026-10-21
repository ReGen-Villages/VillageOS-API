"""The VillageOS managed-microservice contract, for Python services.

A service imports this and writes only its handler. The contract itself is in docs/SERVICE_CONTRACT.md.
"""

from .auth import verifier
from .config import Config, parse_args, usage
