import abc

from .ChatMessage import ChatMessage


class ChatResponseGenerator(abc.ABC):
    @abc.abstractmethod
    def generate_chat_response(self, chat_messages: list[ChatMessage]) -> str:
        pass
