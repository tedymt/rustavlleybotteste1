"""
Cog de Wipe — configuração de datas/horários de wipe, countdown, RCON e timezone.
!sup → RustServer → Wipe
"""
import asyncio
import socket
from datetime import datetime, timezone, timedelta

import discord
from discord.ext import commands
from discord.enums import ChannelType

from config import BOT_OWNER_ID
from utils.storage import get_guild_config, save_guild_config
from utils.wipe_storage import get_wipe_config, save_wipe_config
from utils.translator import t


def can_use_sup(user_id: str, guild_id: str) -> bool:
    if str(user_id) == BOT_OWNER_ID:
        return True
    config = get_guild_config(guild_id)
    return str(user_id) in config.get("allowed_sup_users", [])


def color_from_hex(hex_color: str) -> int:
    hex_color = (hex_color or "#5865F2").lstrip("#")
    try:
        return int(hex_color, 16)
    except ValueError:
        return 0x5865F2


# Fuso horário padrão por região (para conversão)
_TZ_OFFSETS = {
    "BR": -3,
    "EU": 1,
    "US": -5,
}


def _br_to_utc(dt: datetime) -> datetime:
    """Assume dt em horário de Brasília (UTC-3)."""
    if dt.tzinfo is None:
        return dt.replace(tzinfo=timezone(timedelta(hours=-3))).astimezone(timezone.utc)
    return dt.astimezone(timezone.utc)


def _utc_to_local(utc_dt: datetime, offset_hours: int) -> datetime:
    return utc_dt + timedelta(hours=offset_hours)


def _format_countdown(target: datetime) -> str:
    now = datetime.now(timezone.utc)
    if target <= now:
        return "⏰ **WIPE EM ANDAMENTO**"
    delta = target - now
    days = delta.days
    hours, rem = divmod(delta.seconds, 3600)
    mins, secs = divmod(rem, 60)
    parts = []
    if days > 0:
        parts.append(f"**{days}d**")
    parts.append(f"**{hours:02d}h**")
    parts.append(f"**{mins:02d}m**")
    parts.append(f"**{secs:02d}s**")
    return "  ".join(parts)


class WipeConfigView(discord.ui.View):
    """View de configuração do wipe."""

    def __init__(self, bot, guild_id: str, build_embed_func):
        super().__init__(timeout=300)
        self.bot = bot
        self.guild_id = guild_id
        self.build_embed = build_embed_func

    @discord.ui.select(
        cls=discord.ui.ChannelSelect,
        channel_types=[ChannelType.text],
        placeholder="📢 Canal do countdown",
        custom_id="wipe_countdown_ch",
        row=0,
    )
    async def select_countdown_channel(self, interaction: discord.Interaction, select: discord.ui.ChannelSelect):
        ch = select.values[0] if select.values else None
        if not ch:
            return
        cfg = get_wipe_config(self.guild_id)
        cfg["countdown_channel_id"] = str(ch.id)
        save_wipe_config(self.guild_id, cfg)
        await interaction.response.edit_message(
            embed=self.build_embed(self.guild_id),
            view=WipeConfigView(self.bot, self.guild_id, self.build_embed),
        )
        await interaction.followup.send(f"✅ Canal do countdown: {ch.mention}", ephemeral=True)

    @discord.ui.button(label="📅 Definir data/hora (BR)", style=discord.ButtonStyle.primary, row=1, custom_id="wipe_set_datetime")
    async def set_datetime(self, interaction: discord.Interaction, button: discord.ui.Button):
        modal = WipeDatetimeModal(self.guild_id)
        await interaction.response.send_modal(modal)

    @discord.ui.button(label="🖼️ Banner", style=discord.ButtonStyle.secondary, row=1, custom_id="wipe_set_banner")
    async def set_banner(self, interaction: discord.Interaction, button: discord.ui.Button):
        modal = WipeBannerModal(self.guild_id)
        await interaction.response.send_modal(modal)

    @discord.ui.button(label="🖥️ Adicionar RCON", style=discord.ButtonStyle.secondary, row=1, custom_id="wipe_add_rcon")
    async def add_rcon(self, interaction: discord.Interaction, button: discord.ui.Button):
        modal = WipeRconModal(self.guild_id)
        await interaction.response.send_modal(modal)

    @discord.ui.button(label="🔍 Buscar info RCON", style=discord.ButtonStyle.secondary, row=2, custom_id="wipe_fetch_rcon")
    async def fetch_rcon(self, interaction: discord.Interaction, button: discord.ui.Button):
        cog = interaction.client.get_cog("WipeCog")
        if cog:
            await cog._fetch_rcon_info(self.guild_id, interaction)

    @discord.ui.button(label="▶️ Iniciar countdown", style=discord.ButtonStyle.success, row=3, custom_id="wipe_start_countdown")
    async def start_countdown(self, interaction: discord.Interaction, button: discord.ui.Button):
        cog = interaction.client.get_cog("WipeCog")
        if cog:
            await cog._start_countdown(self.guild_id, interaction)

    @discord.ui.button(label="⏹️ Parar countdown", style=discord.ButtonStyle.danger, row=3, custom_id="wipe_stop_countdown")
    async def stop_countdown(self, interaction: discord.Interaction, button: discord.ui.Button):
        cog = interaction.client.get_cog("WipeCog")
        if cog:
            await cog._stop_countdown(self.guild_id, interaction)

    @discord.ui.button(label="⬅️ Voltar", style=discord.ButtonStyle.secondary, row=4, custom_id="wipe_back")
    async def back(self, interaction: discord.Interaction, button: discord.ui.Button):
        from cogs.tickets import SupMainView, _main_embed
        await interaction.response.edit_message(embed=_main_embed(), view=SupMainView(self.bot, self.guild_id))

    async def interaction_check(self, interaction: discord.Interaction) -> bool:
        if not can_use_sup(str(interaction.user.id), self.guild_id):
            await interaction.response.send_message("❌ Sem permissão.", ephemeral=True)
            return False
        return True


class WipeDatetimeModal(discord.ui.Modal, title="Data/hora do wipe (BR)"):
    def __init__(self, guild_id: str):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        self.add_item(discord.ui.TextInput(label="Data (DD/MM/YYYY)", placeholder="Ex: 27/02/2026", required=True, max_length=10))
        self.add_item(discord.ui.TextInput(label="Hora (HH:MM)", placeholder="Ex: 15:00", required=True, max_length=5))

    async def on_submit(self, interaction: discord.Interaction):
        try:
            day, month, year = map(int, self.children[0].value.strip().replace("-", "/").split("/"))
            hour, minute = map(int, self.children[1].value.strip().replace(":", " ").split())
            dt_br = datetime(year, month, day, hour, minute, 0, tzinfo=timezone(timedelta(hours=-3)))
            dt_utc = dt_br.astimezone(timezone.utc)
            cfg = get_wipe_config(self.guild_id)
            cfg["wipe_datetime_br"] = dt_br.isoformat()
            cfg["wipe_datetime_utc"] = dt_utc.isoformat()
            save_wipe_config(self.guild_id, cfg)
            await interaction.response.send_message(
                f"✅ Wipe configurado: **{dt_br.strftime('%d/%m/%Y %H:%M')}** (BR) = **{dt_utc.strftime('%d/%m/%Y %H:%M')}** UTC",
                ephemeral=True,
            )
        except (ValueError, IndexError):
            await interaction.response.send_message("❌ Formato inválido. Use DD/MM/YYYY e HH:MM", ephemeral=True)


class WipeRconModal(discord.ui.Modal, title="Adicionar servidor RCON"):
    def __init__(self, guild_id: str):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        cfg = get_wipe_config(guild_id)
        draft = cfg.get("rcon_draft") or {}
        self.add_item(discord.ui.TextInput(
            label="Host (IP ou domínio)",
            placeholder="Ex: play.rustvalley.com.br",
            default=draft.get("host", ""),
            required=True,
            max_length=100,
        ))
        self.add_item(discord.ui.TextInput(
            label="Porta WebRcon",
            placeholder="Ex: 28016",
            default=draft.get("port", ""),
            required=True,
            max_length=5,
        ))
        self.add_item(discord.ui.TextInput(
            label="Senha RCON",
            placeholder="Senha do webrcon",
            default=draft.get("password", ""),
            required=True,
            max_length=100,
        ))
        self.add_item(discord.ui.TextInput(
            label="Nome (opcional)",
            placeholder="Ex: BR 10X",
            default=draft.get("name", ""),
            required=False,
            max_length=50,
        ))

    async def on_submit(self, interaction: discord.Interaction):
        try:
            host = self.children[0].value.strip()
            port = int(self.children[1].value.strip())
            password = self.children[2].value.strip()
            name = (self.children[3].value or "").strip() or host
            cfg = get_wipe_config(self.guild_id)
            servers = cfg.get("rcon_servers", [])
            servers.append({"host": host, "port": port, "password": password, "name": name})
            cfg["rcon_servers"] = servers[-20:]
            cfg["rcon_draft"] = {"host": host, "port": str(port), "password": password, "name": name}
            save_wipe_config(self.guild_id, cfg)
            await interaction.response.send_message(f"✅ Servidor **{name}** adicionado. Use o botão 'Buscar info RCON' para identificar.", ephemeral=True)
        except ValueError:
            await interaction.response.send_message("❌ Porta inválida.", ephemeral=True)


class WipeBannerModal(discord.ui.Modal, title="Banner do wipe"):
    def __init__(self, guild_id: str):
        super().__init__(timeout=120)
        self.guild_id = guild_id
        cfg = get_wipe_config(guild_id)
        self.add_item(discord.ui.TextInput(
            label="URL do banner",
            placeholder="https://...",
            default=cfg.get("banner_url") or "",
            required=False,
            max_length=500,
        ))

    async def on_submit(self, interaction: discord.Interaction):
        url = (self.children[0].value or "").strip() or None
        cfg = get_wipe_config(self.guild_id)
        cfg["banner_url"] = url
        save_wipe_config(self.guild_id, cfg)
        await interaction.response.send_message(f"✅ Banner {'definido' if url else 'removido'}.", ephemeral=True)


class WipeCog(commands.Cog):
    """Cog de configuração de wipe."""

    def __init__(self, bot):
        self.bot = bot
        self._countdown_tasks: dict[str, asyncio.Task] = {}

    def _build_wipe_embed(self, guild_id: str) -> discord.Embed:
        cfg = get_wipe_config(guild_id)
        gcfg = get_guild_config(guild_id)
        color = color_from_hex(gcfg.get("color", "#5865F2"))

        embed = discord.Embed(
            title="🗓️ Configuração de Wipe",
            description="Configure data/hora do wipe (horário BR), canal do countdown e banner.",
            color=color,
            timestamp=datetime.utcnow(),
        )
        dt_br = cfg.get("wipe_datetime_br")
        dt_utc = cfg.get("wipe_datetime_utc")
        if dt_br:
            try:
                d = datetime.fromisoformat(dt_br.replace("Z", "+00:00"))
                embed.add_field(name="📅 Data/hora (BR)", value=d.strftime("%d/%m/%Y %H:%M"), inline=True)
            except Exception:
                embed.add_field(name="📅 Data/hora (BR)", value=dt_br[:16], inline=True)
        else:
            embed.add_field(name="📅 Data/hora (BR)", value="`Não definido`", inline=True)

        if dt_utc:
            try:
                d_utc = datetime.fromisoformat(dt_utc.replace("Z", "+00:00"))
                if d_utc.tzinfo is None:
                    d_utc = d_utc.replace(tzinfo=timezone.utc)
                embed.add_field(name="🌍 UTC", value=d_utc.strftime("%d/%m/%Y %H:%M"), inline=True)
                br_time = _utc_to_local(d_utc, -3)
                eu_time = _utc_to_local(d_utc, 1)
                us_time = _utc_to_local(d_utc, -5)
                embed.add_field(
                    name="🕐 Fusos horários",
                    value=f"**BR:** {br_time.strftime('%d/%m %H:%M')}\n**EU:** {eu_time.strftime('%d/%m %H:%M')}\n**US:** {us_time.strftime('%d/%m %H:%M')}",
                    inline=False,
                )
            except Exception:
                pass

        ch_id = cfg.get("countdown_channel_id")
        embed.add_field(name="📢 Canal countdown", value=f"<#{ch_id}>" if ch_id else "`Não definido`", inline=True)
        embed.add_field(name="🖼️ Banner", value="✅ Definido" if cfg.get("banner_url") else "`Não definido`", inline=True)
        rcon_servers = cfg.get("rcon_servers", [])
        embed.add_field(
            name="🖥️ Servidores RCON",
            value="\n".join(f"• {s.get('name', s.get('host', '?'))} — {s.get('host', '?')}:{s.get('port', '?')}" for s in rcon_servers[:5]) or "`Nenhum configurado`",
            inline=False,
        )
        embed.set_footer(text="Suporte Valley • Wipe")
        return embed

    async def _start_countdown(self, guild_id: str, interaction: discord.Interaction):
        cfg = get_wipe_config(guild_id)
        ch_id = cfg.get("countdown_channel_id")
        dt_utc = cfg.get("wipe_datetime_utc")
        if not ch_id or not dt_utc:
            return await interaction.response.send_message(
                "❌ Defina o canal do countdown e a data/hora do wipe primeiro.",
                ephemeral=True,
            )
        try:
            target = datetime.fromisoformat(dt_utc.replace("Z", "+00:00"))
            if target.tzinfo is None:
                target = target.replace(tzinfo=timezone.utc)
        except Exception:
            return await interaction.response.send_message("❌ Data/hora inválida.", ephemeral=True)

        await interaction.response.defer(ephemeral=True)
        guild = self.bot.get_guild(int(guild_id))
        if not guild:
            return await interaction.followup.send("❌ Servidor não encontrado.", ephemeral=True)
        channel = guild.get_channel(int(ch_id))
        if not channel or not isinstance(channel, discord.TextChannel):
            return await interaction.followup.send("❌ Canal não encontrado.", ephemeral=True)

        gcfg = get_guild_config(guild_id)
        color = color_from_hex(gcfg.get("color", "#5865F2"))
        banner = cfg.get("banner_url")

        embed = discord.Embed(
            title="⏱️ COUNTDOWN PARA O WIPE",
            description=_format_countdown(target),
            color=color,
            timestamp=datetime.utcnow(),
        )
        if banner:
            embed.set_image(url=banner)
        embed.set_footer(text="Atualiza a cada minuto")
        msg = await channel.send(embed=embed)
        cfg["countdown_message_id"] = str(msg.id)
        save_wipe_config(guild_id, cfg)

        if guild_id in self._countdown_tasks:
            self._countdown_tasks[guild_id].cancel()
        self._countdown_tasks[guild_id] = asyncio.create_task(
            self._countdown_loop(guild_id)
        )
        await interaction.followup.send("✅ Countdown iniciado.", ephemeral=True)

    async def _fetch_rcon_info(self, guild_id: str, interaction: discord.Interaction):
        """Conecta via RCON em todos os servidores e busca nome, jogadores online e localização pelo IP."""
        cfg = get_wipe_config(guild_id)
        servers = cfg.get("rcon_servers", [])
        if not servers:
            return await interaction.response.send_message("❌ Adicione servidores RCON primeiro.", ephemeral=True)
        await interaction.response.defer(ephemeral=True)
        lines = []
        try:
            import aiohttp
            for s in servers:
                host = s.get("host", "")
                port = s.get("port", 28016)
                pwd = s.get("password", "")
                name = s.get("name", host)
                try:
                    info = await self._rcon_fetch_info(host, port, pwd)
                    region = await self._get_region_from_ip(host)
                    line = f"**{name}** — {info.get('players', '?')} online"
                    if region:
                        line += f" | 🌍 {region}"
                    lines.append(line)
                    if info.get("hostname"):
                        s["detected_name"] = info["hostname"][:50]
                    if region:
                        s["region"] = region
                except Exception as e:
                    lines.append(f"**{name}** — ❌ Erro: {str(e)[:50]}")
            save_wipe_config(guild_id, cfg)
            msg = "\n".join(lines) if lines else "Nenhum servidor respondendo."
            await interaction.followup.send(f"🔍 **Info RCON:**\n{msg}", ephemeral=True)
        except ImportError:
            await interaction.followup.send("❌ Instale `webrcon` e `aiohttp` para usar RCON.", ephemeral=True)

    async def _rcon_fetch_info(self, host: str, port: int, password: str) -> dict:
        """Busca server hostname e player count via webrcon."""
        try:
            from webrcon import RconConnector
            result = {"hostname": "", "players": "?"}
            responses = []

            def cb(data):
                responses.append(data)

            connector = RconConnector(host, port, password)
            loop = asyncio.get_event_loop()
            await connector.start(loop)
            try:
                await connector.command("server.hostname", callback=cb)
                await asyncio.sleep(1)
                if responses:
                    result["hostname"] = (responses[-1].get("Message") or "")[:100].strip()
                responses.clear()
                await connector.command("playerlist", callback=cb)
                await asyncio.sleep(1)
                if responses:
                    msg = responses[-1].get("Message") or ""
                    lines = [l for l in msg.split("\n") if l.strip()]
                    result["players"] = str(max(0, len(lines) - 1))
            finally:
                await connector.close()
            return result
        except Exception as e:
            raise RuntimeError(str(e)[:80]) from e

    async def _get_region_from_ip(self, host: str) -> str | None:
        """Obtém região/timezone do IP via ip-api.com (free, no key)."""
        try:
            ip = socket.gethostbyname(host)
        except Exception:
            return None
        try:
            import aiohttp
            async with aiohttp.ClientSession() as session:
                async with session.get(f"http://ip-api.com/json/{ip}?fields=countryCode,timezone", timeout=aiohttp.ClientTimeout(total=5)) as r:
                    if r.status == 200:
                        data = await r.json()
                        tz = data.get("timezone", "")
                        cc = data.get("countryCode", "")
                        if tz:
                            if "America" in tz or cc in ("BR", "US", "CA"):
                                return "US/BR"
                            if "Europe" in tz or cc in ("DE", "FR", "UK", "NL"):
                                return "EU"
                        return cc or None
        except Exception:
            pass
        return None

    async def _stop_countdown(self, guild_id: str, interaction: discord.Interaction):
        if guild_id in self._countdown_tasks:
            self._countdown_tasks[guild_id].cancel()
            del self._countdown_tasks[guild_id]
        cfg = get_wipe_config(guild_id)
        cfg["countdown_message_id"] = None
        save_wipe_config(guild_id, cfg)
        await interaction.response.send_message("✅ Countdown parado.", ephemeral=True)

    async def _countdown_loop(self, guild_id: str):
        cfg = get_wipe_config(guild_id)
        ch_id = cfg.get("countdown_channel_id")
        msg_id = cfg.get("countdown_message_id")
        dt_utc = cfg.get("wipe_datetime_utc")
        if not ch_id or not msg_id or not dt_utc:
            return
        try:
            target = datetime.fromisoformat(dt_utc.replace("Z", "+00:00"))
            if target.tzinfo is None:
                target = target.replace(tzinfo=timezone.utc)
        except Exception:
            return

        gcfg = get_guild_config(guild_id)
        color = color_from_hex(gcfg.get("color", "#5865F2"))
        banner = cfg.get("banner_url")

        while True:
            try:
                await asyncio.sleep(60)
                guild = self.bot.get_guild(int(guild_id))
                if not guild:
                    break
                channel = guild.get_channel(int(ch_id))
                if not channel:
                    break
                msg = await channel.fetch_message(int(msg_id))
                embed = discord.Embed(
                    title="⏱️ COUNTDOWN PARA O WIPE",
                    description=_format_countdown(target),
                    color=color,
                    timestamp=datetime.utcnow(),
                )
                if banner:
                    embed.set_image(url=banner)
                embed.set_footer(text="Atualiza a cada minuto")
                await msg.edit(embed=embed)
                if target <= datetime.now(timezone.utc):
                    break
            except asyncio.CancelledError:
                break
            except Exception as e:
                print(f"[Wipe] Erro countdown {guild_id}: {e}")
                await asyncio.sleep(60)


async def setup(bot):
    await bot.add_cog(WipeCog(bot))
