import openai

from .ChatMessage import ChatMessage
from .ChatResponseGenerator import ChatResponseGenerator
from .UsageTracker import UsageTracker


class OpenAiChatGPT(ChatResponseGenerator):
    def __init__(self, model: str, api_key: str, usage_tracker: UsageTracker):
        """e.g. gpt-4 or gpt-3.5-turbo"""
        openai.api_key = api_key
        self.model = model
        self.usage_tracker = usage_tracker

    @staticmethod
    def __convert_chat_messages_to_dicts(chat_messages: list[ChatMessage]) -> list[dict]:
        return [{"role": message.role, "content": message.content} for message in chat_messages]

    def generate_chat_response(self, chat_messages: list[ChatMessage]) -> str:
        messages = OpenAiChatGPT.__convert_chat_messages_to_dicts(chat_messages)
        completion = openai.ChatCompletion.create(model=self.model, messages=messages)
        self.usage_tracker.track_usage(**completion.usage)
        return completion.choices[0].message.content
