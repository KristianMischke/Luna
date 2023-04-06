import os

from dotenv import load_dotenv

from Luna import Luna
from UsageTrackerDict import UsageTrackerDict
from chat.OpenAiChatGPT import OpenAiChatGPT
from chat.ChatMessage import ChatMessage

load_dotenv()

usage_tracker_dict = UsageTrackerDict()

open_ai_api_key = os.getenv("OPENAI_API_KEY")
open_ai_chat_gpt = OpenAiChatGPT("gpt-3.5-turbo", open_ai_api_key, usage_tracker_dict)

chat_context = []


def receive_message(message: str):
    print(message)
    chat_context.append(ChatMessage(role="assistant", content=message))


while True:
    print("")
    user_input = input()
    chat_context.append(ChatMessage(role="user", content=user_input))

    luna = Luna(chat_context, receive_message, open_ai_chat_gpt)
    luna.respond()
