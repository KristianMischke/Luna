import os

import discord
from dotenv import load_dotenv

from LunaBot import LunaDiscordBot
from UsageTrackerDict import UsageTrackerDict
from chat.OpenAiChatGPT import OpenAiChatGPT

load_dotenv()

intents = discord.Intents.default()
intents.message_content = True

usage_tracker_dict = UsageTrackerDict()

open_ai_api_key = os.getenv("OPENAI_API_KEY")
open_ai_chat_gpt = OpenAiChatGPT("gpt-3.5-turbo", open_ai_api_key, usage_tracker_dict)

client = LunaDiscordBot(intents=intents, chat_response_generator=open_ai_chat_gpt)

discord_api_key = os.getenv("DISCORD_API_KEY")
client.run(discord_api_key)
