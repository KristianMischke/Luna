import os

import discord
from dotenv import load_dotenv

from LunaBot import LunaDiscordBot
from UsageTrackerDict import UsageTrackerDict
from src.LunaBrain import LunaBrain
from src.LunaBrainState import LunaBrainState

load_dotenv()

intents = discord.Intents.default()
intents.message_content = True

usage_tracker_dict = UsageTrackerDict()

open_ai_api_key = os.getenv("OPENAI_API_KEY")

discord_luna_admin_ids = [295009962709614593]

luna_brain = LunaBrain(open_ai_api_key, usage_tracker_dict, LunaBrainState())

client = LunaDiscordBot(intents=intents,
                        discord_luna_admin_ids=discord_luna_admin_ids,
                        luna_brain=luna_brain)

discord_api_key = os.getenv("DISCORD_API_KEY")
client.run(discord_api_key)
