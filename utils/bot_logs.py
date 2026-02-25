"""
Canais de log do bot por tipo (startup, rcon, erros).
Permite configurar um canal por tipo de log por servidor.
"""
from __future__ import annotations

from utils.storage import get_guild_config

LOG_TYPES = ("startup", "rcon", "errors")
_CONFIG_KEYS = {
    "startup": "bot_log_channel_id",
    "rcon": "bot_log_rcon_channel_id",
    "errors": "bot_log_errors_channel_id",
}


def get_log_channel_id(guild_id: str, log_type: str) -> str | None:
    """
    Retorna o ID do canal de log para o tipo indicado.
    log_type: "startup" | "rcon" | "errors"
    """
    if log_type not in LOG_TYPES:
        return None
    config = get_guild_config(guild_id)
    ch_id = config.get(_CONFIG_KEYS[log_type])
    if ch_id:
        return str(ch_id)
    if log_type == "errors":
        return config.get("bot_log_channel_id") and str(config["bot_log_channel_id"]) or None
    return None


async def send_log_embed(bot, guild_id: str, log_type: str, embed, *, fallback_startup: bool = True) -> bool:
    """
    Envia um embed para o canal de log do tipo indicado.
    Para log_type "errors", se o canal de erros não estiver configurado e fallback_startup=True,
    envia para o canal de startup (se existir).
    Retorna True se enviou, False caso contrário.
    """
    import discord

    ch_id = get_log_channel_id(guild_id, log_type)
    if not ch_id and log_type == "errors" and fallback_startup:
        ch_id = get_guild_config(guild_id).get("bot_log_channel_id")
        if ch_id:
            ch_id = str(ch_id)
    if not ch_id:
        return False
    guild = bot.get_guild(int(guild_id))
    if not guild:
        return False
    channel = guild.get_channel(int(ch_id))
    if not channel or not isinstance(channel, discord.TextChannel):
        return False
    try:
        await channel.send(embed=embed)
        return True
    except discord.Forbidden:
        return False
    except Exception:
        return False
