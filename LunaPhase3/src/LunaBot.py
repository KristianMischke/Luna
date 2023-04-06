from typing import Any

import discord
from discord import Intents

from Luna import Luna
from chat.ChatResponseGenerator import ChatResponseGenerator
from chat.ChatMessage import ChatMessage


async def get_context_messages(message: discord.Message, max_context: int) -> list[discord.Message]:
    channel: discord.TextChannel = message.channel
    message_list = []
    async for context_message in channel.history(limit=max_context):
        message_list.insert(0, context_message)
    return message_list


class LunaDiscordBot(discord.Client):

    def __init__(self, *, intents: Intents, chat_response_generator: ChatResponseGenerator , **options: Any):
        super().__init__(intents=intents, **options)

        self._chat_response_generator = chat_response_generator

    async def on_ready(self):
        print(f'Logged on as {self.user}!')

    def convert_to_chat_messages(self, messages: list[discord.Message]) -> list[ChatMessage]:
        chat_messages = []
        for message in messages:
            role = "assistant" if message.author == self.user else "user"
            chat_messages.append(ChatMessage(role=role, content=message.content))
        return chat_messages

    async def on_message(self, message):
        if message.author == self.user:
            return

        print(f"received {message.author}: {message.content}")

        channel: discord.TextChannel = message.channel

        context_messages = await get_context_messages(message, 15)
        chat_context = self.convert_to_chat_messages(context_messages)

        async def luna_response(response: str):
            await channel.send(content=response)

        luna = Luna(chat_context, luna_response, self._chat_response_generator)
        await luna.respond()
