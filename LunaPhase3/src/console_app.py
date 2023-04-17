import asyncio
import os

from dotenv import load_dotenv

from Luna import Luna
from UsageTrackerDict import UsageTrackerDict
from chat.OpenAiChatGPT import OpenAiChatGPT
from chat.ChatMessage import ChatMessage
from LunaBrain import LunaBrain
from LunaBrainState import LunaBrainState

load_dotenv()

usage_tracker_dict = UsageTrackerDict()

open_ai_api_key = os.getenv("OPENAI_API_KEY")
luna_brain = LunaBrain(open_ai_api_key, usage_tracker_dict, LunaBrainState())

chat_context = []


async def luna_respond(message: str):
    print(message)
    chat_context.append(ChatMessage(role="assistant", content="/respond " + message))


async def main():
    while True:
        print("")

        user_prompt = ""

        user_line = input()
        while user_line.strip() != "":
            user_prompt += user_line
            user_line = input()

        chat_context.append(ChatMessage(role="user", content=user_prompt))

        luna = Luna(chat_context, luna_respond, luna_brain)
        await luna.generate_and_execute_response_commands()


if __name__ == '__main__':
    asyncio.run(main())
