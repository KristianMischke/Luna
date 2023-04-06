from typing import Callable


class Luna:

    def __init__(self, respond_callback: Callable[[str], None]):
        self.respond = respond_callback

    def receive_message(self, message: str):
        self.respond(f"echo {message}")
