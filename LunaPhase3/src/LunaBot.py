import random
from typing import Any

import discord
from discord import Intents

from Luna import Luna
from chat.ChatMessage import ChatMessage
from LunaBrain import LunaBrain
from LunaBrainState import Intelligence
from plugins.TenorGif import TenorGif


async def get_context_messages(message: discord.Message, max_context: int) -> list[discord.Message]:
    channel: discord.TextChannel = message.channel
    message_list = []
    async for context_message in channel.history(limit=max_context):
        message_list.insert(0, context_message)
    return message_list


class LunaDiscordBot(discord.Client):

    def __init__(self, *,
                 intents: Intents,
                 discord_luna_admin_ids: list[int],
                 luna_brain: LunaBrain,
                 tenor_gif: TenorGif,
                 **options: Any):
        super().__init__(intents=intents, **options)

        self.luna_brain = luna_brain
        self.discord_luna_admin_ids = discord_luna_admin_ids

        self.tenor_gif = tenor_gif

    async def on_ready(self):
        print(f'Logged on as {self.user}!')

    def _add_username_to_message(self, message: discord.Message) -> str:
        return f"{message.author.display_name}: {message.content}"

    def _convert_to_chat_messages(self, messages: list[discord.Message]) -> list[ChatMessage]:
        chat_messages = []
        for message in messages:
            is_me = message.author == self.user
            role = "assistant" if is_me else "user"
            content = "/respond " + message.content if is_me else self._add_username_to_message(message)
            chat_messages.append(ChatMessage(role=role, content=content))
        return chat_messages

    async def on_message(self, message: discord.Message):
        if message.author == self.user:
            return

        print(f"received {message.author}: {message.content}")

        contains_luna = "luna" in message.content.lower() or self.user in message.mentions

        if message.content.lower().startswith("!set") and message.author.id in self.discord_luna_admin_ids:
            if message.content.lower() == "!set gpt-4":
                self.luna_brain.brain_state.intelligence = Intelligence.Super
            if message.content.lower() == "!set gpt-3":
                self.luna_brain.brain_state.intelligence = Intelligence.ChatGPT
            print(f"set intelligence: {self.luna_brain.brain_state.intelligence}")

        if not contains_luna and random.random() < 0.97:
            print("..skipping")
            return

        channel = message.channel

        context_messages = await get_context_messages(message, 15)
        chat_context = self._convert_to_chat_messages(context_messages)

        async def respond(response: str):
            await channel.send(content=response)

        async def gif(query: str):
            gif_link = self.tenor_gif.find_gif(query)
            await channel.send(content=gif_link)

        callbacks = {
            "respond": respond,
            "gif": gif
        }
        luna = Luna(chat_context, callbacks, self.luna_brain)

        async with channel.typing():
            await luna.generate_and_execute_response_commands()
