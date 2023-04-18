import re
from typing import Callable, Awaitable

from chat.ChatMessage import ChatMessage
from LunaBrain import LunaBrain


class Luna:

    def __init__(self,
                 chat_context: list[ChatMessage],
                 callbacks: dict[str, Callable[[str], Awaitable[None]]],
                 luna_brain: LunaBrain
                 ):
        self._chat_context = chat_context
        self._callbacks = callbacks
        self._brain = luna_brain

    async def generate_and_execute_response_commands(self):
        response = self._brain.generate_chat_response(self._chat_context)
        print("Luna: " + response)

        command_parts = re.split(r"(/\w+)", response)

        prev_part = None
        for part in command_parts:
            if prev_part is not None and prev_part.startswith('/') and len(prev_part) > 1:
                command = prev_part[1:]
                if command in self._callbacks:
                    await self._callbacks[command](part)
            prev_part = part
