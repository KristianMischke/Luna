import typing

import discord
import os
import time
import re
import torch as pt
from transformers import pipeline, set_seed

start = time.time()
summarizer = pipeline("summarization", model="sshleifer/distilbart-cnn-12-6")
end = time.time()
print(end - start)

client = discord.Client()


def resolve_argument_type(value, desired_type):
    if desired_type is int:
        return int(value)

    return None


def parse_command_factory(syntax_options):
    """
        Factory for creating functions to be parsed using regex or nlp
    :param syntax_options:
    :return:
    """
    def parse_command_decorator(func):
        def wrapper(message: discord.Message, **kwargs):

            # get the type hints of the function arguments
            kwargs_type_dict = typing.get_type_hints(func)

            # loop over all syntax options to find which one matches
            for syntax_option in syntax_options:
                pattern = re.compile(syntax_option)
                match = pattern.match(message.content)
                if match:
                    kwargs.update(match.groupdict())

                    # convert the kwargs to the correct type
                    for key, t in kwargs_type_dict.items():
                        if key in kwargs and not isinstance(kwargs[key], t):
                            resolved_argument = resolve_argument_type(kwargs[key], t)
                            if resolved_argument is not None:
                                kwargs[key] = resolved_argument
                    break

            result = func(message, **kwargs)
            return result
        return wrapper
    return parse_command_decorator


@client.event
async def on_ready():
    print('We have logged in as {0.user}'.format(client))


@client.event
async def on_message(message):
    if message.author == client.user:
        return

    if message.content.startswith('!summary'):
        await get_summary(message)


@parse_command_factory([
    r"^!summary (?P<num_messages>\d+) (?P<min_len>\d+)-(?P<max_len>\d+)$",
    r"^!summary (?P<num_messages>\d+)$",
    "^!summary$",
])
async def get_summary(
        message: discord.Message,
        num_messages: int = 20,
        min_len: int = 5,
        max_len: int = 50
) -> bool:
    print(num_messages)

    channel = message.channel
    all_text = ""
    async for message in channel.history(limit=num_messages):
        if message.author != client.user and not message.content.startswith("!"):
            all_text = message.content + "\n" + all_text

    print("\n\n")
    print(all_text)
    print("\n\n")

    # TODO: chunk text and loop because summarizer only supports 1024 max tokens
    start = time.time()
    results = summarizer(all_text, min_length=min_len, max_length=max_len)
    print(results)
    end = time.time()
    print(end - start)

    await channel.send(f"Summary:\n{results[0]['summary_text']}")

    return True



client.run(os.environ["DISCORD_KBOT_TOKEN"])