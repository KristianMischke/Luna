from typing import Callable

from chat.ChatResponseGenerator import ChatResponseGenerator
from chat.ChatMessage import ChatMessage


class Luna:

    def __init__(self,
                 chat_context: list[ChatMessage],
                 respond_callback: Callable[[str], None],
                 response_generator: ChatResponseGenerator
                 ):
        self._chat_context = chat_context
        self._respond = respond_callback
        self._response_generator = response_generator

    def respond(self):
        response = self._response_generator.generate_chat_response(self._chat_context)
        self._respond(response)
