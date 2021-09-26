from __future__ import annotations
from typing import TYPE_CHECKING

import discord

if TYPE_CHECKING:
    from luna import Luna


def get_user_handle_for_gpt3(luna: Luna, user: discord.Member) -> str:
    if user.id == luna.client.user.id:
        return "Luna"
    if user.id in luna.user_data:
        return luna.user_data[user.id]["real_name"]

    return user.nick
