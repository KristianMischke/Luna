import asyncio
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


async def luna_response(message: str):
    print(message)
    chat_context.append(ChatMessage(role="assistant", content=message))


async def main():
    while True:
        print("")

        user_prompt = ""

        user_line = input()
        while user_line.strip() != "":
            user_prompt += user_line
            user_line = input()

        chat_context.append(ChatMessage(role="user", content=user_prompt))

        luna = Luna(chat_context, luna_response, open_ai_chat_gpt)
        await luna.respond()


if __name__ == '__main__':
    asyncio.run(main())
