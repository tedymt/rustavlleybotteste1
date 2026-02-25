"""
Suporte Valley - Bot de Tickets para Discord
Desenvolvido em Python | Armazenamento 100% JSON | Tradução automática PT/EN
"""
__version__ = "1.0.1"

import asyncio
import os
import socket
import sys

# Garante que o diretório do projeto esteja no path (fallback quando rodar main.py diretamente)
_project_root = os.path.dirname(os.path.abspath(__file__))
if _project_root not in sys.path:
    sys.path.insert(0, _project_root)

from datetime import datetime, timezone

import discord
from discord.ext import commands

from config import TOKEN, GROQ_API_KEY, OPENAI_API_KEY, BOT_OWNER_ID
from utils.key_expiry import (
    register_key_first_seen,
    should_warn_expiry,
    get_expiry_warning_message,
)
from utils.storage import get_guild_config
from utils.bot_logs import get_log_channel_id, send_log_embed


def main():
    intents = discord.Intents.default()
    intents.message_content = True
    intents.guilds = True
    intents.members = True

    bot = commands.Bot(command_prefix="!", intents=intents)

    @bot.event
    async def on_ready():
        errors: list[str] = []
        # Carrega cogs só na primeira conexão (evita erro em reconnect)
        if "cogs.tickets" not in bot.extensions:
            try:
                await bot.load_extension("cogs.tickets")
            except Exception as e:
                errors.append(f"Cog Tickets: {e}")
                print(f"[ERRO] Falha ao carregar tickets: {e}")
        if "cogs.agent" not in bot.extensions:
            try:
                await bot.load_extension("cogs.agent")
            except Exception as e:
                errors.append(f"Cog Agent: {e}")
                print(f"[ERRO] Falha ao carregar agent: {e}")
        if "cogs.wipe" not in bot.extensions:
            try:
                await bot.load_extension("cogs.wipe")
            except Exception as e:
                errors.append(f"Cog Wipe: {e}")
                print(f"[ERRO] Falha ao carregar wipe: {e}")
        try:
            await bot.tree.sync()
        except Exception as e:
            errors.append(f"Tree sync: {e}")
            print(f"[ERRO] Tree sync: {e}")

        print(f"✅ Suporte Valley conectado: {bot.user}")
        if errors:
            print("[ERRO] Problemas:", errors)

        # Notifica canal de atualizações quando o bot inicia (deploy/atualização)
        UPDATE_CHANNEL_ID = 1452110008920576100
        try:
            update_channel = bot.get_channel(UPDATE_CHANNEL_ID)
            if update_channel and isinstance(update_channel, discord.TextChannel):
                embed = discord.Embed(
                    title="🔄 Suporte Valley — Atualização",
                    description=f"A versão foi atualizada para **{__version__}**.",
                    color=0x5865F2,
                    timestamp=datetime.now(timezone.utc),
                )
                embed.set_footer(text="Desenvolvido por tedyziim")
                await update_channel.send(embed=embed)
        except discord.Forbidden:
            pass
        except Exception:
            pass

        # Aviso de expiração da chave Groq (90 dias)
        if GROQ_API_KEY:
            register_key_first_seen("groq")
            warn, days = should_warn_expiry("groq")
            if warn and days is not None:
                msg = get_expiry_warning_message("groq", days)
                print(f"\n{msg.replace('**', '').replace('⚠️ ', '')}\n")
                try:
                    owner = await bot.fetch_user(int(BOT_OWNER_ID))
                    await owner.send(msg)
                except Exception:
                    pass

        # Onde o bot está rodando (host/ambiente)
        try:
            _host = os.environ.get("HOSTNAME") or os.environ.get("COMPUTERNAME") or os.environ.get("DISCLOUD_APP_NAME")
            if not _host:
                _host = socket.gethostname()
        except Exception:
            _host = "N/A"
        _where_started = f"{_host} | {datetime.now(timezone.utc).strftime('%d/%m/%Y %H:%M:%S')} UTC"

        # Envia status de startup para o canal de log de cada servidor
        for guild in bot.guilds:
            config = get_guild_config(str(guild.id))
            ch_id = get_log_channel_id(str(guild.id), "startup") or config.get("bot_log_channel_id")
            if not ch_id:
                continue
            channel = guild.get_channel(int(ch_id))
            if not channel or not isinstance(channel, discord.TextChannel):
                continue
            try:
                cfg = get_guild_config(str(guild.id))
                status_lines = [
                    f"**Bot:** {bot.user} conectado",
                    f"**Cogs:** Tickets {'✅' if 'TicketCog' in str(bot.cogs) else '❌'}, Agent {'✅' if 'AgentCog' in str(bot.cogs) else '❌'}, Wipe {'✅' if 'WipeCog' in str(bot.cogs) else '❌'}",
                    f"**Servidores (guilds):** {len(bot.guilds)}",
                    f"**📍 Onde:** `{_where_started}`",
                ]
                config_lines = [
                    f"**Agente:** {'✅ Ativo' if cfg.get('agent_enabled') else '❌ Inativo'}",
                    f"**IA:** {'✅' if cfg.get('agent_ai_enabled') else '❌'} | **APIs:** OpenAI {'✅' if OPENAI_API_KEY else '❌'} | Groq {'✅' if GROQ_API_KEY else '❌'}",
                    f"**Tickets:** Cat. {'✅' if cfg.get('category_id') else '❌'} | Logs {'✅' if cfg.get('logs_channel_id') else '❌'}",
                ]
                embed = discord.Embed(
                    title="🚀 Suporte Valley — Iniciado",
                    description="O bot foi iniciado com sucesso.",
                    color=0x2ECC71,
                    timestamp=datetime.now(timezone.utc),
                )
                embed.add_field(name="📊 Status", value="\n".join(status_lines), inline=False)
                embed.add_field(name="⚙️ Configurações (este servidor)", value="\n".join(config_lines), inline=False)
                if errors:
                    embed.add_field(name="⚠️ Erros", value="\n".join(f"• {e}" for e in errors)[:1024], inline=False)
                    embed.color = 0xE67E22
                embed.set_footer(text="Desenvolvido por tedyziim")
                await channel.send(embed=embed)
            except discord.Forbidden:
                pass
            except Exception as e:
                err_embed = discord.Embed(
                    title="❌ Erro ao enviar log de startup",
                    description=str(e)[:500],
                    color=0xE74C3C,
                    timestamp=datetime.now(timezone.utc),
                )
                err_embed.set_footer(text="Desenvolvido por tedyziim")
                try:
                    await channel.send(embed=err_embed)
                except Exception:
                    pass
                await send_log_embed(bot, str(guild.id), "errors", err_embed, fallback_startup=True)

        # Log de status RCON (em background) para guilds que têm RCON e canal de log RCON
        async def _rcon_log_task():
            await asyncio.sleep(8)
            wipe_cog = bot.get_cog("WipeCog")
            if not wipe_cog or not hasattr(wipe_cog, "_send_rcon_status_log"):
                return
            for guild in bot.guilds:
                try:
                    await wipe_cog._send_rcon_status_log(str(guild.id))
                except Exception:
                    pass
                await asyncio.sleep(1)

        asyncio.create_task(_rcon_log_task())

        # Status / atividade do bot com site + crédito
        try:
            activity = discord.Activity(
                type=discord.ActivityType.watching,
                name="www.rustvalley.com.br ❤️ desenvolvido @tedyziim",
            )
            await bot.change_presence(activity=activity)
        except Exception:
            pass

    if not TOKEN:
        print("❌ Defina DISCORD_TOKEN no arquivo .env")
        return

    bot.run(TOKEN)


if __name__ == "__main__":
    main()
