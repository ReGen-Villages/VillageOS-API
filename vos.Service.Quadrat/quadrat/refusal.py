class Refusal(Exception):
    """A request Quadrat will not answer, with a stable code a caller can branch on and the reason in words."""

    def __init__(self, code, reason):
        super().__init__(f"{code}: {reason}")
        self.code = code
        self.reason = reason

    def __reduce__(self):
        # The default rebuilds an exception from its one message, which this two-argument constructor cannot take.
        return (Refusal, (self.code, self.reason))
