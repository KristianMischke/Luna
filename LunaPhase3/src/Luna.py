from typing import Callable, Awaitable

from chat.ChatMessage import ChatMessage
from src.LunaBrain import LunaBrain


class Luna:

    def __init__(self,
                 chat_context: list[ChatMessage],
                 respond_callback: Callable[[str], Awaitable[None]],
                 luna_brain: LunaBrain
                 ):
        self._chat_context = chat_context
        self._respond = respond_callback
        self._brain = luna_brain

    async def respond(self):
        response = self._brain.generate_chat_response(self._chat_context)
        print("Luna: " + response)
        await self._respond(response)
