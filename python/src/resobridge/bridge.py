"""Bridge client placeholder.

This module will host the high-level client for the ``Bridge`` gRPC service
defined in ``proto/resonite_ai_bridge/v1/bridge.proto`` (currently exposing the
``Ping`` RPC for connectivity smoke tests). The skeleton intentionally does not
import from ``resobridge._generated`` yet so that the public surface compiles
even before ``scripts/gen_proto.sh`` has been run.

TODO: Step 2 — replace this placeholder with a ``BridgeClient`` that opens a
``grpclib`` ``Channel`` over a Unix Domain Socket and calls ``Bridge.Ping``.
"""

__all__ = ["PLACEHOLDER"]

# Sentinel exported so importers can verify the module loaded without
# accidentally relying on internals that will be reshaped in Step 2.
PLACEHOLDER: str = "bridge-client-not-yet-implemented"
